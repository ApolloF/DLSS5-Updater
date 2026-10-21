using System.IO.Compression;
using System.Text.Json;

namespace DLSSUpdater.Core;

/// <summary>A library build together with the DLL an install copies.</summary>
public sealed record ReShadeInstallFile(ReShadeBuild Build, string Path, int Bits);

/// <summary>The newest CI build of crosire/reshade: the run and the digests GitHub publishes for its artifacts.</summary>
public sealed record NightlyInfo(NightlyRun Run, NightlyArtifact? Artifact64, NightlyArtifact? Artifact32);

public sealed partial class ComponentStore
{
    private const string ReShadeDir = "reshade";

    /// <summary>The build the settings choose as the default: a build id, or "channel:nightly" and friends; null = newest Stable add-on build.</summary>
    public Func<string?> SelectedReShade { get; set; } = () => null;

    /// <summary>Newest plain (no add-ons) version reshade.me linked when the releases were last checked.</summary>
    public string? ReShadeNoAddonsVersion { get; internal set; }

    private string? _legacyReShadePin;
    private readonly object _reShadeGate = new();
    private (string Root, IReadOnlyList<ReShadeBuild> Builds)? _reShadeCache;

    private static string ReShadeRoot => System.IO.Path.Combine(AppPaths.Components, ReShadeDir);

    public static string ReShadeBuildDir(ReShadeBuild b) => System.IO.Path.Combine(ReShadeRoot, b.Id);

    public static string ReShadeFilePath(ReShadeBuild b, int bits) =>
        System.IO.Path.Combine(ReShadeBuildDir(b), bits == 32 ? ReShadeBuild.File32 : ReShadeBuild.File64);

    /// <summary>Every readable build, newest first (<see cref="BuildOrder"/>).</summary>
    public IReadOnlyList<ReShadeBuild> ReShadeBuilds
    {
        get
        {
            var root = ReShadeRoot;
            lock (_reShadeGate)
            {
                if (_reShadeCache is { } c && c.Root == root) return c.Builds;
                var builds = ReadReShadeBuilds(root);
                _reShadeCache = (root, builds);
                return builds;
            }
        }
    }

    private void ForgetReShade()
    {
        lock (_reShadeGate) _reShadeCache = null;
    }

    private static IReadOnlyList<ReShadeBuild> ReadReShadeBuilds(string root)
    {
        if (!Directory.Exists(root)) return [];
        var builds = new List<ReShadeBuild>();
        foreach (var file in Directory.EnumerateFiles(root, BuildRecordFile, SearchOption.AllDirectories))
        {
            try
            {
                var b = JsonSerializer.Deserialize(File.ReadAllText(file), JsonCtx.Default.ReShadeBuild);
                var dir = System.IO.Path.GetDirectoryName(file)!;
                if (b is not null && ValidId(b.Id) && System.IO.Path.GetFileName(dir) == b.Id && (b.Has64 || b.Has32)
                    && (!b.Has64 || File.Exists(System.IO.Path.Combine(dir, ReShadeBuild.File64)))
                    && (!b.Has32 || File.Exists(System.IO.Path.Combine(dir, ReShadeBuild.File32))))
                    builds.Add(b);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            { Log.Error($"Could not read ReShade library entry {file}", ex); }
        }
        return BuildOrder.NewestFirst(builds, b => b.Version, b => b.Added, b => b.Id, b => b.ParsedVersion);
    }

    // ---------- choosing a build ----------

    /// <summary>
    /// The pick that applies: the one given (a game's own), else the settings' default, else the build of a ReShade64.dll the user
    /// imported before the library existed (see <see cref="MigrateReShade"/>), else "follow Stable".
    /// </summary>
    public string EffectiveReShadePick(string? pick) =>
        pick ?? SelectedReShade() ?? _legacyReShadePin ?? ReShadePick.ForChannel(ReShadeChannel.Stable);

    /// <summary>The library build a pick names; for a channel the newest build of it. Null when the library has none (yet).</summary>
    public ReShadeBuild? ReShadeBuildFor(string? pick)
    {
        var effective = EffectiveReShadePick(pick);
        if (ReShadePick.ChannelOf(effective) is { } channel) return ReShadeBuilds.FirstOrDefault(b => b.Channel == channel);
        // A pinned build that was removed falls back to the newest Stable build, never to another pin.
        return ReShadeBuilds.FirstOrDefault(b => b.Matches(effective))
               ?? (pick is null ? ReShadeBuilds.FirstOrDefault(b => b.Channel == ReShadeChannel.Stable) : null);
    }

    /// <summary>
    /// The build a default pick uses now. With automatic download off and no default chosen, only an imported build counts (the
    /// old setting: download only when nothing was imported); null when there is none.
    /// </summary>
    public ReShadeBuild? DefaultReShadeBuild()
    {
        if (SelectedReShade() is null && _legacyReShadePin is null && !AutoReShade())
            return ReShadeBuilds.FirstOrDefault(b => b.Channel == ReShadeChannel.Custom);
        return ReShadeBuildFor(null);
    }

    /// <summary>The id of the pin that replaces "Newest Stable" for a user who had imported a ReShade64.dll; handed out once so the caller can save it.</summary>
    public string? TakeLegacyReShadePin()
    {
        var pin = _legacyReShadePin;
        _legacyReShadePin = null;
        return pin;
    }

    // ---------- library writes ----------

    /// <summary>
    /// Stores the DLLs as one build. A build that is already there gets a missing bitness added and nothing else changes
    /// (its label, date and source stay).
    /// </summary>
    private ReShadeBuild RetainReShade(string? dll64, string? dll32, ReShadeChannel channel, string version, ReShadeVerification verification,
        string? origin, string? commit = null)
    {
        if (dll64 is null && dll32 is null) throw new InvalidDataException("No ReShade DLL to keep.");
        var sha64 = dll64 is null ? null : FileUtil.Sha256(dll64);
        var sha32 = dll32 is null ? null : FileUtil.Sha256(dll32);
        var id = sha64 ?? sha32!;
        var dir = System.IO.Path.Combine(ReShadeRoot, id);
        var existing = ReShadeBuilds.FirstOrDefault(b => b.Id == id);
        var build = existing is null
            ? new ReShadeBuild(id, version, channel, sha64, sha32, verification, origin, DateTime.UtcNow, null, commit)
            : existing with { Sha32 = existing.Sha32 ?? sha32, Sha64 = existing.Sha64 ?? sha64 };
        if (existing is not null && existing == build) return existing;
        if (dll64 is not null) CopyIfNew(dll64, System.IO.Path.Combine(dir, ReShadeBuild.File64));
        if (dll32 is not null) CopyIfNew(dll32, System.IO.Path.Combine(dir, ReShadeBuild.File32));
        FileUtil.AtomicWriteText(System.IO.Path.Combine(dir, BuildRecordFile), JsonSerializer.Serialize(build, JsonCtx.Default.ReShadeBuild));
        ForgetReShade();
        if (existing is null && build.Downloaded) NoteAdded(id);
        return build;

        static void CopyIfNew(string source, string dest)
        {
            if (!File.Exists(dest) || !FileUtil.SameFile(source, dest)) FileUtil.AtomicCopy(source, dest);
        }
    }

    /// <summary>Sets the user's own name for a build; empty text goes back to the default label.</summary>
    public ReShadeBuild RenameReShade(string id, string? label)
    {
        var build = ReShadeBuilds.FirstOrDefault(b => b.Id == id) ?? throw new FileNotFoundException("That ReShade build is no longer in the library.");
        var renamed = build with { UserLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim() };
        FileUtil.AtomicWriteText(System.IO.Path.Combine(ReShadeBuildDir(build), BuildRecordFile), JsonSerializer.Serialize(renamed, JsonCtx.Default.ReShadeBuild));
        ForgetReShade();
        return renamed;
    }

    /// <summary>Deletes one build from the library. Games keep the copy they already have.</summary>
    public void RemoveReShade(string id)
    {
        if (!ValidId(id)) throw new InvalidDataException("Invalid ReShade build id.");
        var dir = System.IO.Path.Combine(ReShadeRoot, id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ForgetReShade();
    }

    // ---------- import ----------

    /// <summary>
    /// Adds a build the user got elsewhere: ReShade64.dll or ReShade32.dll (the other bitness is taken from the same folder when it is
    /// there), a folder holding them, a ReShade setup exe, or a zip with the DLLs. Every DLL must pass <see cref="ReShadeSource.Check"/>.
    /// </summary>
    public ReShadeBuild ImportReShade(string source)
    {
        var work = System.IO.Path.Combine(AppPaths.Cache, "reshade-import-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            string? d64 = null, d32 = null;
            if (Directory.Exists(source)) (d64, d32) = FindDlls(source);
            else if (!File.Exists(source)) throw new FileNotFoundException("That file or folder does not exist.", source);
            else if (source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                // The name says the bitness only loosely (people rename), so the file itself decides.
                var pe = Scan.PeReader.Read(source) ?? throw new InvalidDataException($"{System.IO.Path.GetFileName(source)} is not a readable Windows DLL.");
                if (pe.Bits == 32) d32 = source; else d64 = source;
                var (sibling64, sibling32) = FindDlls(System.IO.Path.GetDirectoryName(source)!);
                if (pe.Bits == 32) d64 ??= sibling64; else d32 ??= sibling32;
            }
            else
            {
                var names = source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ReShadeSource.ExtractZip(source, work) : ReShadeSource.ExtractSetup(source, work);
                (d64, d32) = FindDlls(work);
                if (names.Count == 0) throw new InvalidDataException("No ReShade DLL found.");
            }
            foreach (var (path, bits) in new[] { (d64, 64), (d32, 32) })
            {
                if (path is null) continue;
                var check = ReShadeSource.Check(path, bits);
                if (!check.Ok) throw new InvalidDataException($"{System.IO.Path.GetFileName(path)} is not usable as ReShade{bits}.dll: {check.Error}.");
            }
            if (d64 is null && d32 is null) throw new InvalidDataException("No ReShade64.dll or ReShade32.dll found there.");
            var v = FileUtil.ReadVersion(d64 ?? d32!);
            var build = RetainReShade(d64, d32, ReShadeChannel.Custom, ReShadeSource.VersionText(v, full: false), ReShadeVerification.Imported, source);
            Log.Info($"Imported ReShade {build.Version} ({build.Id[..12]}) from {source}");
            return build;
        }
        finally { FileUtil.DeleteDirectory(work); }

        static (string? D64, string? D32) FindDlls(string dir)
        {
            string? Find(string name) => Directory.EnumerateFiles(dir, name, SearchOption.TopDirectoryOnly).FirstOrDefault();
            return (Find(ReShadeBuild.File64), Find(ReShadeBuild.File32));
        }
    }

    // ---------- migration ----------

    /// <summary>
    /// First run with the ReShade library: the ReShade64.dll the user imported into the components folder becomes a Custom build
    /// (and stays what installs use, see <see cref="TakeLegacyReShadePin"/>), and the add-on builds downloaded into the cache become
    /// Stable builds. Nothing is checked beyond being a file, since the old import wasn't either. Files that can't move stay.
    /// </summary>
    public void MigrateReShade()
    {
        var imported = System.IO.Path.Combine(AppPaths.Components, ReShadeFile);
        try
        {
            if (File.Exists(imported))
            {
                var v = FileUtil.ReadVersion(imported);
                var build = RetainReShade(imported, null, ReShadeChannel.Custom, ReShadeSource.VersionText(v, full: false), ReShadeVerification.Imported, "imported before the library");
                File.Delete(imported);
                _legacyReShadePin = build.Id;
                Log.Info($"Moved the imported ReShade64.dll into the ReShade library as {build.Label}");
            }
            var cache = ComponentDir(Component.ReShade);
            if (Directory.Exists(cache))
            {
                foreach (var dir in Directory.EnumerateDirectories(cache).ToList())
                {
                    var dll = System.IO.Path.Combine(dir, ReShadeFile);
                    if (!File.Exists(System.IO.Path.Combine(dir, CompleteMarker)) || !File.Exists(dll)) continue;
                    var tag = System.IO.Path.GetFileName(dir);
                    var build = RetainReShade(dll, null, ReShadeChannel.Stable, FileUtil.ParseTag(tag) is { } pv ? ReShadeSource.VersionText(pv, false) : tag,
                        ReShadeVerification.RecordedAtDownload, ReShadeUrl(tag));
                    FileUtil.DeleteDirectory(dir);
                    Log.Info($"Moved the downloaded ReShade {build.Version} into the ReShade library");
                }
                FileUtil.TryDeleteEmptyDir(cache);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { Log.Error("Could not move the ReShade files into the library", ex); }
    }

    // ---------- download ----------

    /// <summary>Older add-on versions reshade.me still serves (6.0.0 and up), newest first; empty when GitHub can't be reached.</summary>
    public async Task<IReadOnlyList<string>> ReShadeLegacyVersionsAsync(CancellationToken ct)
    {
        try { return ReShadeSource.LegacyVersions(await _gh.GetTagsAsync(ReShadeRepo, 100, ct)); }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Info($"Could not list older ReShade versions ({ex.Message})");
            return [];
        }
    }

    /// <summary>The newest CI run of crosire/reshade's main branch and its artifacts; null when none is found or GitHub can't be reached.</summary>
    public async Task<NightlyInfo?> ResolveReShadeNightlyAsync(CancellationToken ct)
    {
        try
        {
            var (runs, _) = await _gh.GetApiJsonAsync(ReShadeSource.NightlyRunsUrl, ct);
            if (ReShadeSource.ParseNightlyRun(runs) is not { } run) return null;
            var (artifacts, _) = await _gh.GetApiJsonAsync(run.ArtifactsUrl, ct);
            var list = ReShadeSource.ParseNightlyArtifacts(artifacts).Where(a => !a.Expired).ToList();
            return new NightlyInfo(run, list.FirstOrDefault(a => a.Name == ReShadeSource.NightlyArtifact64), list.FirstOrDefault(a => a.Name == ReShadeSource.NightlyArtifact32));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Info($"ReShade nightly: GitHub unreachable ({ex.Message})");
            return null;
        }
    }

    /// <summary>
    /// A library build to install. <paramref name="pick"/> null follows the default; a channel pick fetches the newest build of that
    /// channel when the library lacks it (offline: the newest it has); a build id must be in the library. A build that lacks the
    /// bitness the game needs is completed from the same version on reshade.me. With automatic download off, the default pick uses imports only.
    /// </summary>
    public async Task<ReShadeInstallFile> EnsureReShadeAsync(string? pick, int bits, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        MigrateReShade();
        var effective = EffectiveReShadePick(pick);
        var channel = ReShadePick.ChannelOf(effective);
        if (pick is null && SelectedReShade() is null && _legacyReShadePin is null && !AutoReShade())
        {
            var imported = ReShadeBuilds.FirstOrDefault(b => b.Channel == ReShadeChannel.Custom && b.HasBits(bits)) ?? DefaultReShadeBuild();
            if (imported is null)
                throw new FileNotFoundException("ReShade has not been imported and automatic download is off (Settings → ReShade).");
            return await Require(imported);
        }

        await _locks[Component.ReShade].WaitAsync(ct);
        try
        {
            ReShadeBuild? build;
            if (channel is { } ch)
            {
                var latest = await LatestOfChannelAsync(ch, ct);
                build = latest is not null && !ReShadeBuilds.Any(b => b.Channel == ch && SameVersion(b, ch, latest.Value.Version))
                    ? await DownloadReShadeAsync(ch, latest.Value, progress, ct)
                    : ReShadeBuilds.Where(b => b.Channel == ch).FirstOrDefault();
                if (build is null)
                    throw new InvalidOperationException($"No {ReShadeBuild.ChannelLabel(ch)} ReShade build is known (offline and nothing downloaded). Import ReShade in Settings → ReShade.");
            }
            else
            {
                build = ReShadeBuilds.FirstOrDefault(b => b.Matches(effective))
                        ?? throw new FileNotFoundException("The chosen ReShade build is no longer in the library. Import it again or pick another one.");
            }
            if (!build.HasBits(bits)) build = await CompleteReShadeAsync(build, bits, progress, ct);
            return await Require(build);
        }
        finally { _locks[Component.ReShade].Release(); }

        Task<ReShadeInstallFile> Require(ReShadeBuild build)
        {
            if (!build.HasBits(bits))
                return Task.FromException<ReShadeInstallFile>(new InvalidOperationException(
                    $"This ReShade build ({build.Label}) has no {bits}-bit DLL. Import ReShade{bits}.dll or pick another build."));
            var path = ReShadeFilePath(build, bits);
            if (!File.Exists(path)) return Task.FromException<ReShadeInstallFile>(new FileNotFoundException($"{build.Label} is missing its {System.IO.Path.GetFileName(path)}. Import it again.", path));
            if (!HashCache.Get(path).Equals(bits == 32 ? build.Sha32 : build.Sha64, StringComparison.OrdinalIgnoreCase))
                return Task.FromException<ReShadeInstallFile>(new InvalidDataException("The stored ReShade file changed. Import it again."));
            return Task.FromResult(new ReShadeInstallFile(build, path, bits));
        }
    }

    /// <summary>The old single-file entry: the default build's 64-bit DLL.</summary>
    public async Task<string> EnsureReShadeFileAsync(IProgress<TransferProgress>? progress, CancellationToken ct) =>
        (await EnsureReShadeAsync(null, 64, progress, ct)).Path;

    private static bool SameVersion(ReShadeBuild b, ReShadeChannel ch, string version) => ch == ReShadeChannel.Nightly ? b.Commit == version : b.Version == version;

    /// <summary>The version a moving channel stands at now (offline: what the last check found), with its nightly info.</summary>
    private async Task<(string Version, NightlyInfo? Nightly)?> LatestOfChannelAsync(ReShadeChannel ch, CancellationToken ct)
    {
        switch (ch)
        {
            case ReShadeChannel.Stable:
                return ReShade is { } r ? (r.Tag, null) : null;
            case ReShadeChannel.StableNoAddons:
                return ReShadeNoAddonsVersion is { } v ? (v, null) : null;
            case ReShadeChannel.Nightly:
                var n = await ResolveReShadeNightlyAsync(ct);
                // The nightly's version is only known from the file, so it is told apart by its commit instead.
                return n is null ? null : (n.Run.HeadSha, n);
            default:
                return null;
        }
    }

    private async Task<ReShadeBuild?> DownloadReShadeAsync(ReShadeChannel ch, (string Version, NightlyInfo? Nightly) target, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (ch == ReShadeChannel.Nightly) return await DownloadNightlyAsync(target.Nightly!, progress, ct);
        return await DownloadSetupAsync(target.Version, addon: ch != ReShadeChannel.StableNoAddons, ch, progress, ct);
    }

    /// <summary>A specific older version from reshade.me, into the library as Legacy.</summary>
    public async Task<ReShadeBuild> DownloadReShadeLegacyAsync(string version, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        await _locks[Component.ReShade].WaitAsync(ct);
        try
        {
            if (ReShadeBuilds.FirstOrDefault(b => b.Version == version && b.Channel is ReShadeChannel.Legacy or ReShadeChannel.Stable) is { } have) return have;
            return await DownloadSetupAsync(version, addon: true, ReShadeChannel.Legacy, progress, ct)
                   ?? throw new InvalidDataException($"ReShade {version} could not be downloaded.");
        }
        finally { _locks[Component.ReShade].Release(); }
    }

    private async Task<ReShadeBuild?> DownloadSetupAsync(string version, bool addon, ReShadeChannel channel, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var work = System.IO.Path.Combine(AppPaths.Cache, "reshade-download-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(work);
            var setup = System.IO.Path.Combine(work, $"ReShade_Setup_{version}.exe");
            var url = ReShadeSource.SetupUrl(version, addon);
            await _gh.DownloadAsync(url, setup, $"ReShade {version}", progress, ct);
            progress?.Report(new TransferProgress("Extracting ReShade", null));
            var dlls = System.IO.Path.Combine(work, "dll");
            await Task.Run(() => ReShadeSource.ExtractSetup(setup, dlls), ct);
            var d64 = System.IO.Path.Combine(dlls, ReShadeBuild.File64);
            string? d32 = System.IO.Path.Combine(dlls, ReShadeBuild.File32);
            if (!File.Exists(d64)) throw new InvalidDataException("ReShade64.dll missing from the ReShade setup.");
            if (!File.Exists(d32)) d32 = null;
            foreach (var (path, bits) in new[] { (d64, 64), (d32, 32) })
            {
                if (path is null) continue;
                var check = ReShadeSource.Check(path, bits);
                if (!check.Ok) throw new InvalidDataException($"ReShade {version}: {System.IO.Path.GetFileName(path)} was refused: {check.Error}.");
                if (bits == 64 && check.Version is { } v && FileUtil.ParseTag(version) is { } want && (v.Major != want.Major || v.Minor != want.Minor))
                    throw new InvalidDataException($"reshade.me served ReShade {FileUtil.Format(v)} when {version} was asked for.");
            }
            var build = RetainReShade(d64, d32, channel, version, ReShadeVerification.RecordedAtDownload, url);
            Log.Info($"Cached ReShade {version} ({ReShadeBuild.ChannelLabel(channel)}), SHA-256 {build.Id[..12]}…");
            return build;
        }
        finally { FileUtil.DeleteDirectory(work); }
    }

    private async Task<ReShadeBuild> DownloadNightlyAsync(NightlyInfo info, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var work = System.IO.Path.Combine(AppPaths.Cache, "reshade-download-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(work);
            var dlls = System.IO.Path.Combine(work, "dll");
            var verified = true;
            foreach (var artifact in new[] { info.Artifact64, info.Artifact32 })
            {
                if (artifact is null) throw new InvalidDataException("The newest ReShade CI build has no 32-bit and 64-bit artifact (expired?).");
                var zip = System.IO.Path.Combine(work, artifact.Name.Replace(' ', '_').Replace('(', '_').Replace(')', '_') + ".zip");
                await _gh.DownloadAsync(ReShadeSource.NightlyZipUrl(artifact.Name), zip, $"ReShade nightly ({artifact.Name})", progress, ct);
                if (artifact.Sha256 is null) verified = false;
                else if (!(await Task.Run(() => FileUtil.Sha256(zip), ct)).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{artifact.Name}: SHA-256 does not match the digest GitHub publishes (a newer CI run may have replaced it; check again).");
                await Task.Run(() => ReShadeSource.ExtractZip(zip, dlls), ct);
            }
            var (d64, d32) = (System.IO.Path.Combine(dlls, ReShadeBuild.File64), System.IO.Path.Combine(dlls, ReShadeBuild.File32));
            foreach (var (path, bits) in new[] { (d64, 64), (d32, 32) })
            {
                if (!File.Exists(path)) throw new InvalidDataException($"ReShade{bits}.dll missing from the nightly artifact.");
                var check = ReShadeSource.Check(path, bits);
                if (!check.Ok) throw new InvalidDataException($"ReShade nightly: ReShade{bits}.dll was refused: {check.Error}.");
            }
            var version = ReShadeSource.VersionText(FileUtil.ReadVersion(d64), full: true);
            var build = RetainReShade(d64, d32, ReShadeChannel.Nightly, version,
                verified ? ReShadeVerification.PublishedDigest : ReShadeVerification.RecordedAtDownload,
                ReShadeSource.NightlyZipUrl(ReShadeSource.NightlyArtifact64), info.Run.HeadSha);
            Log.Info($"Cached ReShade nightly {version} ({info.Run.HeadSha[..7]}){(verified ? "" : ", unverified source")}");
            return build;
        }
        finally { FileUtil.DeleteDirectory(work); }
    }

    /// <summary>Fetches the other bitness of a downloaded build from the same version; the DLL we already have must still be byte-identical.</summary>
    private async Task<ReShadeBuild> CompleteReShadeAsync(ReShadeBuild build, int bits, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!build.Downloaded || build.Channel == ReShadeChannel.Nightly)
            return build;
        var fetched = await DownloadSetupAsync(build.Version, addon: build.Addon, build.Channel, progress, ct);
        // The same build gets the new file added (same id); anything else means the site changed what it serves.
        if (fetched is null || fetched.Id != build.Id)
            throw new InvalidDataException($"reshade.me now serves a different ReShade {build.Version} than the one in the library, so its {bits}-bit DLL can't be added. Pick another build.");
        return ReShadeBuilds.First(b => b.Id == build.Id);
    }
}
