using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DLSSUpdater.Scan;
using SharpCompress.Archives;

namespace DLSSUpdater.Core;

/// <summary>The three places DXVK builds come from. Ids double as the pick values for "follow this channel's newest build".</summary>
public enum DxvkChannel { Stable, Development, Lilium }

public static class DxvkChannels
{
    public static string Id(DxvkChannel c) => c switch { DxvkChannel.Development => "development", DxvkChannel.Lilium => "lilium", _ => "stable" };

    public static string Label(DxvkChannel c) => c switch { DxvkChannel.Development => "Development", DxvkChannel.Lilium => "Lilium HDR", _ => "Stable" };

    public static DxvkChannel? Parse(string? id) =>
        Enum.GetValues<DxvkChannel>().Cast<DxvkChannel?>().FirstOrDefault(c => Id(c!.Value).Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>A pick that names a channel ("stable", "development", "lilium") rather than one build.</summary>
    public static bool IsChannel(string? pick) => Parse(pick) is not null;
}

/// <summary>Which DXVK dlls a graphics API needs, and where they sit in a build.</summary>
public static class DxvkDlls
{
    public static readonly string[] All = ["d3d8.dll", "d3d9.dll", "d3d10core.dll", "d3d11.dll", "dxgi.dll"];

    /// <summary>DXVK translates Direct3D 8 to 11 only; DirectX 12, Vulkan and OpenGL games have nothing for it to translate.</summary>
    public static bool Applies(GraphicsApis primary) => primary is GraphicsApis.Dx8 or GraphicsApis.Dx9 or GraphicsApis.Dx10 or GraphicsApis.Dx11;

    /// <summary>DX8 d3d8, DX9 d3d9, DX10 d3d10core + dxgi, DX11 d3d11 + dxgi (DXVK's README, "How to use").</summary>
    public static IReadOnlyList<string> For(GraphicsApis primary) => primary switch
    {
        GraphicsApis.Dx8 => ["d3d8.dll"],
        GraphicsApis.Dx9 => ["d3d9.dll"],
        GraphicsApis.Dx10 => ["d3d10core.dll", "dxgi.dll"],
        GraphicsApis.Dx11 => ["d3d11.dll", "dxgi.dll"],
        _ => [],
    };

    /// <summary>The DXVK dlls this game's install record lists (file names); null when DXVK isn't installed there.</summary>
    public static IReadOnlyCollection<string>? InstalledIn(InstallManifest? m) =>
        m?.Component(ComponentRegistry.Dxvk) is { } c ? c.Files.Select(f => Path.GetFileName(f.Path)).Where(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList() : null;

    /// <summary>The folder of a build for a game's bitness: x32 or x64.</summary>
    public static string Arch(bool is32Bit) => is32Bit ? "x32" : "x64";
}

/// <summary>A DXVK build the app can download: the archive and the SHA-256 GitHub published for it.</summary>
public sealed record DxvkRelease(DxvkChannel Channel, string Id, string Label, string Version, DateTime? Published, string AssetName, string Url, string Sha256, string? Notes);

/// <summary>
/// DXVK as a component. Builds come from three channels: Stable (doitsujin/dxvk releases, a .tar.gz), Development (the newest successful
/// master run of its Artifacts workflow, through nightly.link, a .zip of the dlls) and Lilium HDR (EndlesslyFlowering/dxvk releases, a
/// .7z). Every download is checked against the SHA-256 GitHub publishes for the asset (no digest = no install). A build is the folder
/// components\dxvk\&lt;id&gt;\pkg\{x32,x64}\ holding the five dlls of each bitness; the folder's .complete record is its metadata.
/// Layouts, checked against the real releases: the Stable tarball holds dxvk-&lt;version&gt;/x32 and /x64; the Development zip holds x32 and x64 at its top
/// (plus a native Linux tarball); the Lilium .7z holds normal/x32 and normal/x64 and the author's preset .conf files.
/// </summary>
public sealed partial class DxvkLibrary(GitHubClient github)
{
    public const string StableRepo = "doitsujin/dxvk";
    public const string LiliumRepo = "EndlesslyFlowering/dxvk";
    public const string DevWorkflow = "artifacts.yml";
    public const string DevBranch = "master";
    private const string CompleteMarker = ".complete";
    private const string PackageDir = "pkg";

    public static readonly string DevRunsUrl =
        $"https://api.github.com/repos/{StableRepo}/actions/workflows/{DevWorkflow}/runs?branch={DevBranch}&event=push&status=success&per_page=1";

    public static string Root => Path.Combine(AppPaths.Components, ComponentRegistry.Dxvk);
    private static string DirOf(string id) => Path.Combine(Root, SafeName(id));
    private static string SafeName(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    [GeneratedRegex(@"^dxvk-(\d+(?:\.\d+)+)\.tar\.gz$", RegexOptions.IgnoreCase)]
    private static partial Regex StableAsset();

    [GeneratedRegex(@"^v?(?<dxvk>\d+(?:\.\d+)*)-HDR-mod-v?(?<mod>\d+(?:\.\d+)*)", RegexOptions.IgnoreCase)]
    private static partial Regex LiliumTag();

    [GeneratedRegex(@"(?<![\d.])(\d+\.\d+(?:\.\d+)?)", RegexOptions.None)]
    private static partial Regex VersionInName();

    // ---------- what is released ----------

    /// <summary>The newest stable release with the plain <c>dxvk-&lt;version&gt;.tar.gz</c> (not the native Linux one) and a published SHA-256.</summary>
    public static DxvkRelease? PickStable(IEnumerable<GhRelease> releases)
    {
        var found = new List<DxvkRelease>();
        foreach (var r in releases.Where(r => !r.Draft && !r.Prerelease))
        {
            if (r.Assets.FirstOrDefault(a => StableAsset().IsMatch(a.Name)) is not { } asset || asset.Sha256 is not { Length: 64 } sha) continue;
            var version = r.TagName.TrimStart('v', 'V');
            found.Add(new DxvkRelease(DxvkChannel.Stable, r.TagName, $"DXVK {version}", r.TagName, r.PublishedAt, asset.Name, asset.Url, sha, r.Body));
        }
        return BuildOrder.NewestFirst(found, f => f.Version, f => f.Published, f => f.Id).FirstOrDefault();
    }

    /// <summary>
    /// The newest Lilium release: the plain .7z, not the "-gplasync" one (that variant adds the async shader patch, which this app has no
    /// options for). A release without a digest is skipped.
    /// </summary>
    public static DxvkRelease? PickLilium(IEnumerable<GhRelease> releases)
    {
        var found = new List<DxvkRelease>();
        foreach (var r in releases.Where(r => !r.Draft && !r.Prerelease))
        {
            var asset = r.Assets.FirstOrDefault(a => a.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && !a.Name.Contains("gplasync", StringComparison.OrdinalIgnoreCase));
            if (asset?.Sha256 is not { Length: 64 } sha) continue;
            var m = LiliumTag().Match(r.TagName);
            var label = m.Success ? $"DXVK {m.Groups["dxvk"].Value} · Lilium HDR {m.Groups["mod"].Value}" : $"DXVK {r.TagName} · Lilium HDR";
            found.Add(new DxvkRelease(DxvkChannel.Lilium, "lilium-" + r.TagName, label, r.TagName, r.PublishedAt, asset.Name, asset.Url, sha, r.Body));
        }
        return BuildOrder.NewestFirst(found, f => f.Version, f => f.Published, f => f.Id).FirstOrDefault();
    }

    /// <summary>
    /// The development build of a CI run: the artifact named <c>dxvk-master-&lt;sha&gt;</c> that holds the dlls (the run also publishes a
    /// native Linux tarball inside it, which is ignored). nightly.link serves it without a GitHub login; the digest GitHub publishes for the artifact is the check.
    /// </summary>
    public static DxvkRelease? PickDevelopment(NightlyRun run, IEnumerable<NightlyArtifact> artifacts)
    {
        var artifact = artifacts.FirstOrDefault(a => !a.Expired && a.Sha256 is { Length: 64 } && a.Name.StartsWith("dxvk-master-", StringComparison.OrdinalIgnoreCase));
        if (artifact is null) return null;
        var sha = run.HeadSha.Length >= 7 ? run.HeadSha[..7] : run.HeadSha;
        return new DxvkRelease(DxvkChannel.Development, "dev-" + run.HeadSha[..Math.Min(12, run.HeadSha.Length)], $"DXVK development {sha}", "dev-" + sha, run.Created,
            artifact.Name + ".zip", DevZipUrl(run.Id, artifact.Name), artifact.Sha256!, null);
    }

    public static string DevZipUrl(long runId, string artifactName) =>
        $"https://nightly.link/{StableRepo}/actions/runs/{runId}/{Uri.EscapeDataString(artifactName)}.zip";

    /// <summary>The newest downloadable build of a channel; null when there is none usable or GitHub can't be reached (the library keeps what it has).</summary>
    public async Task<DxvkRelease?> LatestAsync(DxvkChannel channel, CancellationToken ct)
    {
        try
        {
            switch (channel)
            {
                case DxvkChannel.Stable: return PickStable(await github.GetReleasesAsync(StableRepo, 15, ct));
                case DxvkChannel.Lilium: return PickLilium(await github.GetReleasesAsync(LiliumRepo, 15, ct));
                default:
                {
                    var (runs, _) = await github.GetApiJsonAsync(DevRunsUrl, ct);
                    if (ReShadeSource.ParseNightlyRun(runs) is not { } run) return null;
                    var (artifacts, _) = await github.GetApiJsonAsync(run.ArtifactsUrl, ct);
                    return PickDevelopment(run, ReShadeSource.ParseNightlyArtifacts(artifacts));
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            Log.Info($"DXVK {DxvkChannels.Label(channel)}: GitHub unreachable ({ex.Message})");
            return channel switch
            {
                DxvkChannel.Stable => PickStable(github.CachedReleases(StableRepo)),
                DxvkChannel.Lilium => PickLilium(github.CachedReleases(LiliumRepo)),
                _ => null,
            };
        }
    }

    // ---------- the library ----------

    /// <summary>The builds in the library, newest first within each channel (channels are separate runs: their versions don't compare). Only metadata is read.</summary>
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
                        || !Path.GetFileName(dir).Equals(SafeName(meta.Tag), StringComparison.OrdinalIgnoreCase)) continue;
                    var channel = DxvkChannels.Parse(meta.Channel) ?? DxvkChannel.Stable;
                    var label = meta.Label ?? meta.Tag;
                    found.Add(new LibraryBuild(LibraryKind.Dxvk, meta.Tag, label + (meta.Imported ? " (imported)" : ""), VersionOf(meta),
                        meta.Published ?? meta.Fetched, dir, DxvkChannels.Id(channel), Downloaded: !meta.Imported));
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                { Log.Error($"Could not read DXVK record {marker}", ex); }
            }
        return found.GroupBy(b => b.Group)
            .OrderBy(g => DxvkChannels.Parse(g.Key))
            .SelectMany(g => BuildOrder.NewestFirst(g, b => b.Version, b => b.Date, b => b.Id))
            .ToList();
    }

    private static string VersionOf(CachedComponent meta) => meta.Imported ? meta.Label ?? meta.Tag : meta.Tag;

    public static DxvkChannel ChannelOf(LibraryBuild build) => DxvkChannels.Parse(build.Group) ?? DxvkChannel.Stable;

    /// <summary>True for a build of the Lilium fork, or an imported build that has its options: the HDR keys of dxvk.conf only mean something there.</summary>
    public static bool HasLiliumOptions(LibraryBuild build) => ChannelOf(build) == DxvkChannel.Lilium;

    public static LibraryBuild? Find(string? id) => id is null ? null : Builds().FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The newest build already in the library for a pick: a build id, or a channel (null = the app-wide default channel, Stable).</summary>
    public static LibraryBuild? Choose(string? pick, string? defaultPick = null)
    {
        var builds = Builds();
        var chosen = pick ?? defaultPick;
        if (chosen is not null && !DxvkChannels.IsChannel(chosen))
            return builds.FirstOrDefault(b => b.Id.Equals(chosen, StringComparison.OrdinalIgnoreCase));
        var channel = DxvkChannels.Parse(chosen) ?? DxvkChannel.Stable;
        return builds.FirstOrDefault(b => ChannelOf(b) == channel);
    }

    /// <summary>The channel a pick follows, or null when it pins one build.</summary>
    public static DxvkChannel? FollowedChannel(string? pick, string? defaultPick) =>
        (pick ?? defaultPick) is { } p && !DxvkChannels.IsChannel(p) ? null : DxvkChannels.Parse(pick ?? defaultPick) ?? DxvkChannel.Stable;

    /// <summary>"Auto (DXVK 3.1.1)" for the pickers: what the default installs now.</summary>
    public static string AutoLabel(string? defaultPick)
    {
        var channel = DxvkChannels.IsChannel(defaultPick) || defaultPick is null ? DxvkChannels.Parse(defaultPick) ?? DxvkChannel.Stable : (DxvkChannel?)null;
        var have = Choose(defaultPick);
        return $"Auto ({have?.Label ?? (channel is { } c ? $"newest {DxvkChannels.Label(c)}, downloaded on install" : "the pinned build is missing")})";
    }

    public static string PackageOf(LibraryBuild build) => Path.Combine(build.Dir, PackageDir);

    public static string DllPath(LibraryBuild build, string arch, string dll) => Path.Combine(PackageOf(build), arch, dll);

    /// <summary>
    /// Takes a build out of the library. A default or game pick on it goes back to Auto, never to another build (docs/design.md section 4);
    /// the caller saves the settings.
    /// </summary>
    public static void Remove(LibraryBuild build, AppSettings settings)
    {
        if (FileUtil.IsUnder(Path.GetFullPath(build.Dir), Root)) FileUtil.DeleteDirectory(build.Dir);
        if (settings.DefaultBuild(ComponentRegistry.Dxvk)?.Equals(build.Id, StringComparison.OrdinalIgnoreCase) == true)
            settings.SetDefaultBuild(ComponentRegistry.Dxvk, null);
        foreach (var game in settings.Games.Values)
            if (game.BuildPick(ComponentRegistry.Dxvk)?.Equals(build.Id, StringComparison.OrdinalIgnoreCase) == true)
                game.SetBuildPick(ComponentRegistry.Dxvk, null);
    }

    /// <summary>
    /// Downloads <paramref name="release"/> into the library unless it is there already: the archive is hashed against GitHub's digest before
    /// anything is read from it, and only the dlls (each by its exact name, from the x32 and x64 folders) are taken.
    /// </summary>
    public async Task<LibraryBuild> EnsureAsync(DxvkRelease release, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (Find(release.Id) is { } have) return have;
        var dir = DirOf(release.Id);
        if (Directory.Exists(dir)) FileUtil.DeleteDirectory(dir);
        Directory.CreateDirectory(dir);
        var archive = Path.Combine(dir, release.AssetName);
        try
        {
            await github.DownloadAsync(release.Url, archive, $"DXVK {release.Id}", progress, ct);
            progress?.Report(new TransferProgress("Verifying DXVK", null));
            await Task.Run(() =>
            {
                if (!FileUtil.Sha256(archive).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{release.AssetName}: refused, its SHA-256 doesn't match the digest GitHub published for it.");
                Extract(archive, Path.Combine(dir, PackageDir));
            }, ct);
            var meta = new CachedComponent { Tag = release.Id, Published = release.Published, Fetched = DateTime.UtcNow, Label = release.Label, Channel = DxvkChannels.Id(release.Channel) };
            FileUtil.AtomicWriteText(Path.Combine(dir, CompleteMarker), JsonSerializer.Serialize(meta, JsonCtx.Default.CachedComponent));
            Log.Info($"Cached {release.Label}");
        }
        catch
        {
            FileUtil.DeleteDirectory(dir);
            throw;
        }
        finally { FileUtil.TryDeleteFile(archive); }
        return Find(release.Id)!;
    }

    // ---------- archives ----------

    private sealed record Candidate(string Prefix, string Arch, string Dll, string Path);

    [GeneratedRegex(@"^(?<prefix>(?:[^/]+/)*?)(?<arch>x32|x64)/(?<dll>d3d8|d3d9|d3d10core|d3d11|dxgi)\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex DllEntry();

    private static string Norm(string path) => path.Replace('\\', '/').TrimStart('.', '/');

    /// <summary>Calls <paramref name="visit"/> for every file of an archive or folder with its path and a way to write it out to a file (valid only during the call).</summary>
    private static void Walk(string source, Action<string, Action<string>> visit)
    {
        if (Directory.Exists(source))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
            foreach (var file in Directory.EnumerateFiles(source, "*", options))
                visit(Norm(Path.GetRelativePath(source, file)), dest => File.Copy(file, dest, true));
            return;
        }
        var name = source.ToLowerInvariant();
        if (name.EndsWith(".zip"))
        {
            using var zip = ZipFile.OpenRead(source);
            foreach (var e in zip.Entries.Where(e => e.Name.Length > 0)) visit(Norm(e.FullName), dest => e.ExtractToFile(dest, true));
        }
        else if (name.EndsWith(".tar.gz") || name.EndsWith(".tgz"))
        {
            using var gz = new GZipStream(File.OpenRead(source), CompressionMode.Decompress);
            using var tar = new TarReader(gz);
            while (tar.GetNextEntry() is { } e)
                if (e.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile) visit(Norm(e.Name), dest => e.ExtractToFile(dest, true));
        }
        else if (name.EndsWith(".7z"))
        {
            using var archive = ArchiveFactory.OpenArchive(source);
            foreach (var e in archive.Entries.Where(e => !e.IsDirectory && e.Key is not null)) visit(Norm(e.Key!), dest => CopyTo(e.OpenEntryStream(), dest));
        }
        else throw new InvalidDataException($"{Path.GetFileName(source)} isn't a folder, .zip, .tar.gz or .7z.");
    }

    /// <summary>
    /// Takes the ten dlls (five per bitness) out of a DXVK archive or folder into <paramref name="pkg"/>\x32 and \x64, each by its exact name.
    /// A release with several variants in it (the Lilium fork's "normal" and others) is read from the "normal" one, else the shallowest;
    /// one missing a dll is refused whole, so a build is never half there.
    /// </summary>
    internal static void Extract(string source, string pkg)
    {
        var all = new List<Candidate>();
        Walk(source, (path, _) =>
        {
            if (DllEntry().Match(path) is { Success: true } m)
                all.Add(new Candidate(m.Groups["prefix"].Value, m.Groups["arch"].Value.ToLowerInvariant(), m.Groups["dll"].Value.ToLowerInvariant() + ".dll", path));
        });
        var wanted = new[] { "x32", "x64" }.SelectMany(a => DxvkDlls.All.Select(d => (Arch: a, Dll: d))).ToList();
        var complete = all.GroupBy(c => c.Prefix, StringComparer.OrdinalIgnoreCase)
            .Where(g => wanted.All(w => g.Any(c => c.Arch == w.Arch && c.Dll == w.Dll)))
            .OrderByDescending(g => g.Key.Equals("normal/", StringComparison.OrdinalIgnoreCase)).ThenBy(g => g.Key.Length).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? throw new InvalidDataException(all.Count == 0
                ? $"No DXVK dlls found in {Path.GetFileName(source)} (expected x32 and x64 folders with d3d8, d3d9, d3d10core, d3d11 and dxgi)."
                : $"{Path.GetFileName(source)} doesn't hold all of d3d8, d3d9, d3d10core, d3d11 and dxgi for both x32 and x64, so it isn't what this app expects.");
        var paths = complete.ToDictionary(c => c.Path, c => c, StringComparer.OrdinalIgnoreCase);
        Walk(source, (path, write) =>
        {
            if (!paths.TryGetValue(path, out var c)) return;
            var dest = Path.Combine(pkg, c.Arch, c.Dll);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            write(dest);
        });
        foreach (var (arch, dll) in wanted)
            if (!File.Exists(Path.Combine(pkg, arch, dll))) throw new InvalidDataException($"{dll} ({arch}) could not be read from {Path.GetFileName(source)}.");
    }

    private static void CopyTo(Stream input, string dest)
    {
        using (input)
        using (var output = File.Create(dest)) input.CopyTo(output);
    }

    // ---------- import ----------

    /// <summary>
    /// Adds a build the user got themselves: a release archive (.tar.gz, .zip, .7z) or the extracted folder. The ten dlls are copied byte for byte;
    /// the build's id is the SHA-256 of the 64-bit dxgi.dll, so importing the same files again does nothing. Whether it is a Lilium build is read from
    /// its dlls (the fork's option names are in them). Imports are never removed by the automatic cleanup.
    /// </summary>
    public static LibraryBuild Import(string source)
    {
        if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("That file or folder does not exist.", source);
        var staging = Root + ".new";
        if (Directory.Exists(staging)) FileUtil.DeleteDirectory(staging);
        try
        {
            var pkg = Path.Combine(staging, PackageDir);
            Extract(source, pkg);
            var sha = FileUtil.Sha256(Path.Combine(pkg, "x64", "dxgi.dll"));
            var id = "imported-" + sha[..12];
            var lilium = ContainsText(Path.Combine(pkg, "x64", "d3d11.dll"), DxvkConf.LiliumSignature);
            var named = VersionInName().Match(Path.GetFileNameWithoutExtension(source.TrimEnd('\\', '/')).Replace(".tar", "")) is { Success: true } v ? v.Value : null;
            var label = $"DXVK {named ?? "imported " + sha[..8]}{(lilium ? " · Lilium HDR" : "")}";
            var meta = new CachedComponent { Tag = id, Fetched = DateTime.UtcNow, Imported = true, Label = label, Channel = DxvkChannels.Id(lilium ? DxvkChannel.Lilium : DxvkChannel.Stable) };
            FileUtil.AtomicWriteText(Path.Combine(staging, CompleteMarker), JsonSerializer.Serialize(meta, JsonCtx.Default.CachedComponent));
            var dir = DirOf(id);
            if (Directory.Exists(dir)) return Find(id) ?? throw new InvalidDataException("That build is in the library but unreadable; remove it and import again.");
            Directory.CreateDirectory(Root);
            Directory.Move(staging, dir);
            Log.Info($"Imported {label}");
            return Find(id)!;
        }
        finally
        {
            if (Directory.Exists(staging)) FileUtil.DeleteDirectory(staging);
        }
    }

    private static bool ContainsText(string path, string ascii) => File.ReadAllBytes(path).AsSpan().IndexOf(Encoding.ASCII.GetBytes(ascii)) >= 0;

    /// <summary>What the install puts in a game: the dlls of the effective API from the build's folder for the game's bitness, beside the exe.</summary>
    public static IReadOnlyList<(string Source, string Dest)> Files(LibraryBuild build, GameInfo game, string targetDir) =>
        DxvkDlls.For(game.Effective.PrimaryApi)
            .Select(dll => (DllPath(build, DxvkDlls.Arch(game.Effective.Is32Bit), dll), Path.Combine(targetDir, dll)))
            .ToList();
}
