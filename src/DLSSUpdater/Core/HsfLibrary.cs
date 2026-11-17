using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>
/// HSF (Capt-Hunter's universal frame generation, a ReShade add-on that feeds NVIDIA DLSS Frame Generation into games without it).
/// Its licence is private and invite-only: the app never downloads, mirrors or redistributes it, the user imports the release they were
/// given (the .zip or the extracted folder) and the app copies it byte for byte. Every import is a build of <see cref="LibraryKind.Hsf"/>:
/// components\hsf\&lt;sha256 of hsf.addon64&gt;\pkg\ holds the files the app places and the folder's .complete record is the metadata.
/// docs/addons/hsf.md has the layout.
/// </summary>
public static partial class HsfLibrary
{
    public const string Addon64 = "hsf.addon64";
    public const string Addon32 = "hsf.addon32";
    public const string StreamlineDir = "HSF_streamline";
    public const string ShaderRoot = "reshade-shaders";
    /// <summary>The one Streamline file the layout check asks for: a folder without it isn't an HSF release.</summary>
    public const string StreamlineMarker = "sl.interposer.dll";
    private const string CompleteMarker = ".complete";
    private const string PackageDir = "pkg";

    /// <summary>What the registry says the component owns, relative to the install folder.</summary>
    public static readonly string[] OwnedFiles = [Addon64, Addon32, StreamlineDir + @"\*", ShaderRoot + @"\Shaders\HSF*.fx"];

    public static string Root => Path.Combine(AppPaths.Components, ComponentRegistry.Hsf);
    private static string DirOf(string id) => Path.Combine(Root, id);

    /// <summary>A release path (relative, '/' separators) the app keeps: the two add-ons, the HSF_streamline folder and the reshade-shaders folder.</summary>
    private static bool Wanted(string rel) =>
        rel.Equals(Addon64, StringComparison.OrdinalIgnoreCase) || rel.Equals(Addon32, StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith(StreamlineDir + "/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith(ShaderRoot + "/", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\d+(?:\.\d+)+")]
    private static partial Regex VersionPattern();

    /// <summary>"HSF-0.9.4" or "HSF 0.9.4 (preview)" -> "0.9.4"; null when the name holds no dotted version.</summary>
    internal static string? VersionOf(string? name) => name is not null && VersionPattern().Match(name) is { Success: true } m ? m.Value : null;

    /// <summary>The builds in the library, newest first (see <see cref="BuildOrder"/>). Only metadata is read.</summary>
    public static IReadOnlyList<LibraryBuild> Builds()
    {
        var found = new List<LibraryBuild>();
        if (Directory.Exists(Root))
            foreach (var dir in Directory.EnumerateDirectories(Root))
            {
                var marker = Path.Combine(dir, CompleteMarker);
                if (dir.EndsWith(".dlssu-delete", StringComparison.OrdinalIgnoreCase) || !File.Exists(marker)) continue;
                try
                {
                    if (JsonSerializer.Deserialize(File.ReadAllText(marker), JsonCtx.Default.CachedComponent) is not { } meta || string.IsNullOrWhiteSpace(meta.Tag)
                        || !Path.GetFileName(dir).Equals(meta.Tag, StringComparison.OrdinalIgnoreCase)) continue;
                    var label = meta.Label ?? "imported";
                    found.Add(new LibraryBuild(LibraryKind.Hsf, meta.Tag, label, label, meta.Fetched, dir));
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                { Log.Error($"Could not read HSF record {marker}", ex); }
            }
        return BuildOrder.NewestFirst(found, b => b.Version, b => b.Date, b => b.Id);
    }

    /// <summary>The build installs use: the newest in the library (there is no default or per-game pick).</summary>
    public static LibraryBuild? Newest() => Builds().FirstOrDefault();

    public static string PackageOf(LibraryBuild build) => Path.Combine(build.Dir, PackageDir);

    // ---------- import ----------

    /// <summary>
    /// Adds a release the user got: the .zip, the extracted release folder (or a folder holding it) or <c>hsf.addon64</c> in it. The
    /// files are copied byte for byte; the build's id is the SHA-256 of <c>hsf.addon64</c>, so importing the same add-on again replaces
    /// that build's set. The label is the version in the zip's or folder's name ("0.9.4"), else "imported". Never checked against
    /// anything and never removed by the cleanup: the user chose the files.
    /// </summary>
    public static LibraryBuild Import(string source)
    {
        var staging = Root + ".new";
        if (Directory.Exists(staging)) FileUtil.DeleteDirectory(staging);
        try
        {
            var pkg = Path.Combine(staging, PackageDir);
            string? version;
            if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                version = VersionOf(Path.GetFileNameWithoutExtension(source));
                CopyRelease(ExtractZip(source, Path.Combine(staging, "raw")), pkg);
            }
            else
            {
                var folder = ReleaseFolder(source) ?? throw new InvalidDataException(
                    $"{Addon64} not found in {source}. Choose the extracted HSF release folder, {Addon64} or the release zip.");
                version = VersionOf(Path.GetFileName(folder.TrimEnd('\\', '/')));
                CopyRelease(folder, pkg);
            }
            var raw = Path.Combine(staging, "raw");
            if (Directory.Exists(raw)) FileUtil.DeleteDirectory(raw);
            var id = FileUtil.Sha256(Path.Combine(pkg, Addon64)).ToLowerInvariant();
            var meta = new CachedComponent { Tag = id, Fetched = DateTime.UtcNow, Imported = true, Label = version ?? "imported" };
            FileUtil.AtomicWriteText(Path.Combine(staging, CompleteMarker), JsonSerializer.Serialize(meta, JsonCtx.Default.CachedComponent));
            var dir = DirOf(id);
            if (Directory.Exists(dir)) FileUtil.DeleteDirectory(dir);
            Directory.CreateDirectory(Root);
            Directory.Move(staging, dir);
            Log.Info($"Imported HSF {meta.Label}");
            return Builds().First(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(staging)) FileUtil.DeleteDirectory(staging);
        }
    }

    /// <summary>Unpacks the kept files of a zip whose files are at its top or one folder down; returns the folder that holds <c>hsf.addon64</c>.</summary>
    private static string ExtractZip(string zip, string raw)
    {
        string prefix;
        using (var archive = ZipFile.OpenRead(zip))
        {
            prefix = archive.Entries.Select(e => e.FullName.Replace('\\', '/'))
                .Where(n => n.EndsWith(Addon64, StringComparison.OrdinalIgnoreCase) && n.Count(c => c == '/') <= 1 && (n.Length == Addon64.Length || n[^(Addon64.Length + 1)] == '/'))
                .Select(n => n[..^Addon64.Length]).OrderBy(n => n.Length).FirstOrDefault()
                ?? throw new InvalidDataException($"{Addon64} isn't in {Path.GetFileName(zip)}. Choose the HSF release zip.");
        }
        ZipStage.Extract(zip, raw, name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Wanted(name[prefix.Length..]));
        return Path.Combine(raw, prefix.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Copies the kept files of a release folder into <paramref name="pkg"/> and checks the layout.</summary>
    private static void CopyRelease(string folder, string pkg)
    {
        if (!File.Exists(Path.Combine(folder, Addon64))) throw new InvalidDataException($"{Addon64} is missing from {folder}.");
        if (!File.Exists(Path.Combine(folder, StreamlineDir, StreamlineMarker)))
            throw new InvalidDataException($"HSF: {StreamlineDir}\\{StreamlineMarker} is missing from {folder}, so it isn't an HSF release.");
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (!Wanted(rel) || File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) continue;
            FileUtil.AtomicCopy(file, Path.Combine(pkg, rel.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    /// <summary>The folder holding <c>hsf.addon64</c>: the one chosen, the folder of the file chosen, or a single subfolder of it.</summary>
    private static string? ReleaseFolder(string source)
    {
        if (File.Exists(source)) source = Path.GetDirectoryName(Path.GetFullPath(source))!;
        if (!Directory.Exists(source)) return null;
        if (File.Exists(Path.Combine(source, Addon64))) return source;
        var inner = Directory.EnumerateDirectories(source).Where(d => File.Exists(Path.Combine(d, Addon64))).Take(2).ToList();
        return inner.Count == 1 ? inner[0] : null;
    }

    /// <summary>Takes a build out of the library (a game that has it installed keeps its files; Remove in the game undoes them).</summary>
    public static void Remove(LibraryBuild build)
    {
        if (FileUtil.IsUnder(Path.GetFullPath(build.Dir), Root)) FileUtil.DeleteDirectory(build.Dir);
    }

    // ---------- plan ----------

    /// <summary>
    /// What HSF puts in a game, following its README: <c>hsf.addon64</c> (64-bit game) or <c>hsf.addon32</c> (32-bit game) in ReShade's
    /// add-on folder, the HSF_streamline folder beside the exe (32-bit games need it too) and the reshade-shaders folder when the release has one.
    /// A game whose bitness isn't known gets the 64-bit add-on.
    /// </summary>
    public static ComponentPlan Plan(LibraryBuild build, GameInfo game, string targetDir, string addonDir)
    {
        var pkg = PackageOf(build);
        var is32 = game.Effective.Is32Bit;
        var addon = is32 ? Addon32 : Addon64;
        if (!File.Exists(Path.Combine(pkg, addon)))
            throw new InvalidDataException($"The imported HSF {build.Label} has no {addon}, which this {(is32 ? "32" : "64")}-bit game needs. Import a release that has it.");
        var files = new List<(string Source, string Dest)> { (Path.Combine(pkg, addon), Path.Combine(addonDir, addon)) };
        foreach (var file in Directory.EnumerateFiles(pkg, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(pkg, file);
            var inFolder = rel.Contains(Path.DirectorySeparatorChar);
            if (inFolder && Wanted(rel.Replace('\\', '/'))) files.Add((file, Path.Combine(targetDir, rel)));
        }
        return new ComponentPlan(ComponentRegistry.Hsf)
        {
            BuildId = build.Id,
            Version = build.Label,
            Files = files,
            Extras = new Dictionary<string, string> { ["bits"] = is32 ? "32" : game.Effective.Bits == 0 ? "64 (unknown bitness)" : "64" },
        };
    }
}

/// <summary>Installs and removes HSF in a game: ReShade 6.8 with add-on support first, then one component plan from the newest imported build.</summary>
public sealed class HsfService(ComponentStore store, Installer installer)
{
    public static readonly AddonPurpose Purpose = new("HSF", "HSF needs ReShade 6.8 or newer (its README says so). Update ReShade for this game first.", new Version(6, 8));

    private readonly ReShadeAddonHost host = new(store, installer);

    public async Task<ComponentPlan> InstallAsync(GameInfo game, string targetDir, HdrReShade reshade, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (game.AntiCheat is { } ac) throw new InvalidOperationException($"{game.Name} has {ac} anti-cheat: HSF is never used in online games with anti-cheat, and no add-on is installed in them.");
        var build = HsfLibrary.Newest() ?? throw new InvalidOperationException("No HSF release is imported. Import it in Settings → Local components first.");
        await host.EnsureAsync(game, targetDir, reshade, Purpose, progress, ct);
        var plan = HsfLibrary.Plan(build, game, targetDir, ReShadeAddonPath.Resolve(FileUtil.Normalize(targetDir), game.Root));
        if (game.Effective.Bits == 0) Log.Warn($"{game.Name}: bitness unknown, installing the 64-bit HSF add-on only.");
        progress?.Report(new TransferProgress($"Installing HSF to {game.Name}", null));
        await installer.InstallComponentAsync(game, targetDir, plan, ct);
        return plan;
    }

    public Task<ComponentUndo> RemoveAsync(GameInfo game, string targetDir, CancellationToken ct) =>
        installer.RemoveComponentAsync(game, targetDir, ComponentRegistry.Hsf, ct);
}

/// <summary>What HSF needs in a game and on this PC, from its README. Shared by the method advisor and the game page.</summary>
public static class HsfNeeds
{
    public const string FlowSource = "A flow source you add yourself for DirectX 9/11 games and DirectX 12 games without DLSS: GITC Uplift (this app installs it), iMMERSE LaunchPad or LumeniteFX Kernel";
    public const string Gpu = "RTX 40 or newer; on RTX 20/30 through the MFG Unlock add-on";
    public const string Display = "Borderless or windowed display mode (no exclusive fullscreen)";
    public const string AntiCheat = "Never in online games or games with anti-cheat";

    /// <param name="needsFlowSource">The game isn't a DirectX 12 game with its own DLSS (<see cref="NeedsFlowSource"/>).</param>
    public static IReadOnlyList<string> For(bool needsFlowSource, GpuInfo gpu)
    {
        var list = new List<string>();
        if (needsFlowSource) list.Add(FlowSource);
        if (gpu.IsKnown && gpu.IsRtx && gpu.Tier < RtxTier.Rtx40) list.Add("RTX 20/30: install the MFG Unlock add-on (HSF's README: through MFG-Unlocker, installed by you)");
        list.Add(Display);
        return list;
    }

    /// <summary>README step 5: DirectX 9/11 games, and DirectX 12 games without DLSS. An unknown API counts as needing one.</summary>
    public static bool NeedsFlowSource(GraphicsApis api, bool nativeDlss) => api != GraphicsApis.Dx12 || !nativeDlss;
}
