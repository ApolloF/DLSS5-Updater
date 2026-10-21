using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace DLSSUpdater.Core;

/// <summary>
/// What the app knows about a component without looking at a game: its stable id (the key in install records, install-refs.json
/// and per-game settings), its name, the library its builds live in and the files it places.
/// </summary>
public sealed record ComponentDefinition(string Id, string Name)
{
    /// <summary>The build library; null when the component has none (config edits, a single cached file).</summary>
    public LibraryKind? Library { get; init; }
    /// <summary>Short name in the game list's mod label (<see cref="InstallManifest.ModLabel"/>); null = not shown there.</summary>
    public string? Label { get; init; }
    /// <summary>
    /// Files the component places, relative to the install folder; '*' and '?' match as in a file search, and a pattern
    /// may hold a folder ("reshade-shaders\Shaders\X_*.fx"). Install records list the exact files; these tell another
    /// install's or the user's copy of them apart.
    /// </summary>
    public IReadOnlyList<string> OwnedFiles { get; init; } = [];
    /// <summary>Stored in the record's per-component fields, not in <see cref="InstallManifest.Components"/> (see <see cref="LegacyComponents"/>).</summary>
    public bool Legacy { get; init; }

    public bool OwnsFile(string relative)
    {
        // '\' escapes in a simple expression, so both sides use '/'.
        var rel = relative.Replace('\\', '/').TrimStart('/');
        return OwnedFiles.Any(p => FileSystemName.MatchesSimpleExpression(p.Replace('\\', '/'), rel, ignoreCase: true));
    }
}

/// <summary>
/// Every component the app installs. A new one registers here once (<see cref="Register"/>); install records, removal,
/// <see cref="KnownInstalls"/>, the build cleanup and per-game picks then work for it by id.
/// </summary>
public static class ComponentRegistry
{
    public const string OptiScaler = "optiscaler";
    public const string ReShade = "reshade";
    public const string MfgUnlock = "mfg-unlock";
    public const string DlssNr = "dlss-nr";
    public const string Dlss = "dlss";
    public const string Streamline = "streamline";
    public const string RenoDx = "renodx-dlss";
    public const string Lecram = "lecram-dlss5";
    public const string Dfc = "dfc";
    public const string Uplift = "gitc-uplift";

    private static readonly ConcurrentDictionary<string, ComponentDefinition> Defs = new(StringComparer.OrdinalIgnoreCase);

    static ComponentRegistry()
    {
        // The components that keep their own fields in the install record.
        Register(new(OptiScaler, "OptiScaler") { Library = LibraryKind.OptiScaler, Legacy = true, OwnedFiles = ["OptiScaler.ini", @"OptiScaler\*", @"Licenses\*"] });
        Register(new(ReShade, "ReShade") { Legacy = true, OwnedFiles = [ComponentStore.ReShadeFile, "ReShade.ini"] });
        Register(new(RenoDx, "ShortFuse RenoDX DLSS") { Library = LibraryKind.RenoDx, Legacy = true, OwnedFiles = [ComponentStore.RenoDxFile, .. ComponentStore.LegacyRenoDxFiles] });
        Register(new(Lecram, "Lecram DLSS5") { Library = LibraryKind.Lecram, Legacy = true, OwnedFiles = ComponentStore.LecramFiles });
        Register(new(Dfc, "Deep Fried Chicken")
        {
            Library = LibraryKind.Dfc, Legacy = true,
            OwnedFiles = [.. ComponentStore.DfcPackageFiles, ComponentStore.DfcConfigFile, ComponentStore.DfcNeuralDir + @"\*",
                Path.Combine(ComponentStore.DfcShaderDir, ComponentStore.DfcShaderPrefix + "*")],
        });
        Register(new(Uplift, "GITC Uplift") { Library = LibraryKind.Uplift, Legacy = true, OwnedFiles = ComponentStore.UpliftFiles });
        Register(new(MfgUnlock, "MFG Unlock") { Legacy = true, OwnedFiles = [ComponentStore.MfgFile] });
        Register(new(Streamline, "Streamline") { Legacy = true, OwnedFiles = ["sl.*.dll"] });
        Register(new(DlssNr, "NR runtime") { Library = LibraryKind.DlssNr, Legacy = true, OwnedFiles = [ComponentStore.DlssNrFile, Path.Combine(ComponentStore.UpliftNrDir, ComponentStore.DlssNrFile)] });
        Register(new(Dlss, "DLSS") { Legacy = true, OwnedFiles = ComponentStore.DlssFiles });
    }

    /// <summary>
    /// Adds a component. Registering the same definition again is fine; another definition under a taken id is a bug.
    /// Ids are stable: they are saved in every game's install record.
    /// </summary>
    public static ComponentDefinition Register(ComponentDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.Id) || def.Id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_')))
            throw new ArgumentException($"\"{def.Id}\" is not a component id (letters, digits, '-', '.', '_').", nameof(def));
        var added = Defs.GetOrAdd(def.Id, def);
        if (!ReferenceEquals(added, def) && !SameDefinition(added, def))
            throw new InvalidOperationException($"Component id \"{def.Id}\" is already registered for {added.Name}.");
        return added;
    }

    private static bool SameDefinition(ComponentDefinition a, ComponentDefinition b) =>
        a.Id == b.Id && a.Name == b.Name && a.Library == b.Library && a.Label == b.Label && a.Legacy == b.Legacy
        && a.OwnedFiles.SequenceEqual(b.OwnedFiles, StringComparer.OrdinalIgnoreCase);

    public static ComponentDefinition? Find(string? id) => id is not null && Defs.TryGetValue(id, out var d) ? d : null;

    public static IReadOnlyCollection<ComponentDefinition> All => Defs.Values.ToList();

    /// <summary>The components whose builds live in <paramref name="kind"/>.</summary>
    public static IEnumerable<ComponentDefinition> OfLibrary(LibraryKind kind) => Defs.Values.Where(d => d.Library == kind);
}
