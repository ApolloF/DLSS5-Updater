using System.Globalization;

namespace DLSSUpdater.Core;

/// <summary>
/// What the app changed in a game's driver profile, so it can put it back: for every id it wrote, the value the profile
/// itself held before the first write (or "absent"). Kept in the game's <c>nvidia-driver</c> component pick as
/// <c>prev:&lt;id&gt;</c> = <c>absent</c> | hex value.
/// </summary>
public sealed class NvidiaRecord
{
    private const string PreviousPrefix = "prev:";
    private const string Absent = "absent";

    /// <summary>Id -> value before the app's first write; null = the profile had no value of its own.</summary>
    public Dictionary<uint, uint?> Previous { get; } = [];

    public bool IsEmpty => Previous.Count == 0;

    public static NvidiaRecord Load(GameOverride game)
    {
        var record = new NvidiaRecord();
        foreach (var (key, value) in game.PickOf(ComponentRegistry.NvidiaDriver)?.Options ?? [])
        {
            if (!key.StartsWith(PreviousPrefix, StringComparison.Ordinal)) continue;
            if (!uint.TryParse(key.AsSpan(PreviousPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)) continue;
            if (value == Absent) record.Previous[id] = null;
            else if (uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) record.Previous[id] = v;
        }
        return record;
    }

    /// <summary>Writes the record into the game's settings (the caller saves them); an empty record removes its entry.</summary>
    public void Save(GameOverride game)
    {
        var options = (game.PickOf(ComponentRegistry.NvidiaDriver)?.Options ?? [])
            .Where(o => !o.Key.StartsWith(PreviousPrefix, StringComparison.Ordinal))
            .ToDictionary(o => o.Key, o => o.Value);
        foreach (var (id, value) in Previous)
            options[$"{PreviousPrefix}{id:X8}"] = value is { } v ? v.ToString("X8", CultureInfo.InvariantCulture) : Absent;
        game.SetPickOptions(ComponentRegistry.NvidiaDriver, options.Count == 0 ? null : options);
    }
}

/// <summary>The game the driver profile belongs to: the exe the profile is matched on, a name for a new profile, and where the record lives.</summary>
public sealed record NvidiaGame(string Exe, string Name, GameOverride Settings);

/// <summary>What the driver says about the ids of one game's profile.</summary>
/// <param name="User">Values the profile itself holds.</param>
/// <param name="Fallback">What applies without them: NVIDIA's shipped value for this game, else the global profile.</param>
/// <param name="GameDefaults">Ids whose fallback is NVIDIA's shipped game value.</param>
/// <param name="Known">Ids this driver defines; the others are not offered.</param>
public sealed record NvidiaSnapshot(string Status, bool HasProfile, Dictionary<uint, uint?> User, Dictionary<uint, uint?> Fallback, HashSet<uint> GameDefaults, HashSet<uint> Known);

/// <param name="Mismatches">Ids the driver did not keep as written (id, wanted, got; null = absent).</param>
public sealed record NvidiaResult(IReadOnlyList<(uint Id, uint? Wanted, uint? Got)> Mismatches)
{
    public bool Verified => Mismatches.Count == 0;
}

/// <summary>
/// Reads and writes a game's NVIDIA driver profile through <see cref="IDrsProvider"/> and remembers what it replaced.
/// Nothing is written until <see cref="Apply"/>, <see cref="Reset"/> or <see cref="SetFrameRateLimit"/> is called.
/// The profile is the one <see cref="NvDrs"/> already matches or creates for the exe.
/// </summary>
public sealed class NvidiaProfile(IDrsProvider drs)
{
    public static NvidiaProfile Real { get; } = new(new NvDrsProvider());

    public bool Available => drs.Available;

    public NvidiaSnapshot Read(string exe, IEnumerable<uint> ids)
    {
        using var session = drs.Open();
        var baseProfile = session.BaseProfile();
        var profile = session.FindProfileForExe(exe);
        var user = new Dictionary<uint, uint?>();
        var fallback = new Dictionary<uint, uint?>();
        var gameDefaults = new HashSet<uint>();
        var known = new HashSet<uint>();
        foreach (var id in ids.Distinct())
        {
            var global = session.Read(baseProfile, id);
            var own = profile is { } p ? session.Read(p, id) : null;
            if (global is not null || own is not null) known.Add(id);
            if (own is { IsUserValue: true } u) user[id] = u.Value;
            if (own is { Location: 0, Predefined: true } pre)
            {
                fallback[id] = pre.Value;
                gameDefaults.Add(id);
            }
            else fallback[id] = global?.Value;
        }
        var status = profile is { } gp
            ? $"Driver profile: {session.ProfileName(gp)}"
            : $"No NVIDIA profile for {Path.GetFileName(exe)} yet. Apply creates one.";
        return new NvidiaSnapshot(status, profile is not null, user, fallback, gameDefaults, known);
    }

    /// <summary>
    /// Writes <paramref name="changes"/> (id -> value, null = remove the profile's own value) in one session and saves once, so a failure
    /// leaves the driver untouched. The value each id had before is recorded the first time it is written, and every id is read back afterwards.
    /// </summary>
    public NvidiaResult Apply(NvidiaGame game, IReadOnlyDictionary<uint, uint?> changes)
    {
        if (changes.Count == 0) return new NvidiaResult([]);
        var record = NvidiaRecord.Load(game.Settings);
        var previous = new Dictionary<uint, uint?>();
        using (var session = drs.Open())
        {
            var profile = session.GetOrCreateProfileForExe(game.Exe, game.Name);
            foreach (var (id, value) in changes)
            {
                if (!record.Previous.ContainsKey(id)) previous[id] = session.Get(profile, id);
                if (value is { } v) session.Set(profile, id, v);
                else session.Delete(profile, id);
            }
            session.Save();
        }

        foreach (var (id, value) in previous) record.Previous[id] = value;
        record.Save(game.Settings);
        return Verify(game.Exe, changes);
    }

    /// <summary>Puts every setting the app wrote back to what the profile held before, then forgets the record.</summary>
    public NvidiaResult Reset(NvidiaGame game) => Restore(game, null);

    /// <summary>Puts the listed ids (all recorded ones when null) back to their recorded previous value.</summary>
    public NvidiaResult Restore(NvidiaGame game, IReadOnlyCollection<uint>? ids)
    {
        var record = NvidiaRecord.Load(game.Settings);
        var back = record.Previous.Where(p => ids is null || ids.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        if (back.Count == 0) return new NvidiaResult([]);
        using (var session = drs.Open())
        {
            // No profile means nothing of ours is left to undo.
            if (session.FindProfileForExe(game.Exe) is { } profile)
            {
                foreach (var (id, value) in back)
                {
                    if (value is { } v) session.Set(profile, id, v);
                    else session.Delete(profile, id);
                }
                session.Save();
            }
        }

        foreach (var id in back.Keys) record.Previous.Remove(id);
        record.Save(game.Settings);
        return Verify(game.Exe, back);
    }

    /// <summary>
    /// Caps the game's frame rate through the driver's Frame Rate Limiter V3 (0 = off, otherwise 20..1000), recording what was there before.
    /// Shared with the frame-limiter code; the caller saves the settings.
    /// </summary>
    public NvidiaResult SetFrameRateLimit(NvidiaGame game, int fps)
    {
        if (fps != 0 && fps is < NvidiaProfileCatalog.MinFrameRate or > NvidiaProfileCatalog.MaxFrameRate)
            throw new ArgumentOutOfRangeException(nameof(fps), fps, $"The driver limiter takes 0 or {NvidiaProfileCatalog.MinFrameRate}-{NvidiaProfileCatalog.MaxFrameRate} FPS.");
        return Apply(game, new Dictionary<uint, uint?> { [NvidiaProfileCatalog.FrameRateLimit] = (uint)fps });
    }

    /// <summary>Puts the game's driver limiter back to what it was before <see cref="SetFrameRateLimit"/>.</summary>
    public NvidiaResult RestoreFrameRateLimit(NvidiaGame game) => Restore(game, [NvidiaProfileCatalog.FrameRateLimit]);

    private NvidiaResult Verify(string exe, IReadOnlyDictionary<uint, uint?> expected)
    {
        var after = Read(exe, expected.Keys);
        var wrong = expected.Where(e => after.User.GetValueOrDefault(e.Key) != e.Value)
            .Select(e => (e.Key, e.Value, after.User.GetValueOrDefault(e.Key))).ToList();
        return new NvidiaResult(wrong);
    }
}
