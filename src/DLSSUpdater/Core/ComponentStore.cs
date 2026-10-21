using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DLSSUpdater.Core;

public enum Component { OptiScaler, MfgUnlock, Dlss, Streamline, ReShade, Uplift }

public sealed class ReleaseInfo
{
    public required string Tag { get; init; }
    public bool Prerelease { get; init; }
    public DateTime? Published { get; init; }
    public bool FromCache { get; init; }
    /// <summary>Resolved from the API cache because GitHub failed: downloadable, but maybe not the latest.</summary>
    public bool Stale { get; init; }
    /// <summary>Release notes as published (markdown); null when offline or the source has none.</summary>
    public string? Notes { get; init; }
    public List<(string Name, string Url, string? Sha256)> Files { get; init; } = [];

    public Version? Version => FileUtil.ParseTag(Tag);
}

/// <summary>Written to a cache folder once its download is complete and verified.</summary>
public sealed class CachedComponent
{
    public string Tag { get; set; } = "";
    public bool Prerelease { get; set; }
    public DateTime? Published { get; set; }
    public DateTime Fetched { get; set; }
    /// <summary>The user brought this build in (DLSS5-Feeder only so far): kept byte for byte and never removed by the automatic cleanup.</summary>
    public bool Imported { get; set; }
    /// <summary>The version of an imported build, as read from its files.</summary>
    public string? Label { get; set; }
    /// <summary>The channel a build belongs to, for libraries that have several (DXVK: stable, development, lilium).</summary>
    public string? Channel { get; set; }
}

/// <summary>Resolves the latest releases, downloads them once per tag and holds the user-supplied files.</summary>
public sealed partial class ComponentStore
{
    public const string LegacyOptiRepo = "wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass";
    public const string OptiRepo = "ShyVortex/OptiScaler-DLSSNR-PreSR-Multipass";
    public const string UpliftRepo = "ghostinthecamera/GITC-Uplift";
    public const string MfgRepo = "mavismmg/MFGAdaUnlock-RenoDx";
    public const string DlssRepo = "NVIDIA/DLSS";
    public const string StreamlineRepo = "NVIDIA-RTX/Streamline";
    public const string ReShadeRepo = "crosire/reshade";
    public const string ReShadeSite = "https://reshade.me";

    public const string DlssNrFile = "nvngx_dlssnr.dll";
    public const string ReShadeFile = "ReShade64.dll";
    public const string MfgFile = "renodx-mfgunlock.addon64";
    /// <summary>ShortFuse's DLSS add-on with neural rendering; import-only.</summary>
    public const string RenoDxFile = "renodx-dlss.addon64";
    /// <summary>Other ShortFuse-family add-ons that also inject NR or replaced the add-on earlier; never installed by this app.</summary>
    public static readonly string[] LegacyRenoDxFiles = ["renodx-dlssfix.addon64"];
    /// <summary>Lecram's DLSS5 add-on: v10 and older, and v11 and newer (never both in one game).</summary>
    public const string Lecram10File = "renodx-dlss5.addon64";
    public const string Lecram11File = "dlss5-nr.addon64";
    public static readonly string[] LecramFiles = [Lecram10File, Lecram11File];
    public const string UpliftFile = "gitc-uplift.addon64";
    public const string UpliftHelper = "gitc-uplift-helper64.exe";
    public const string UpliftNrDir = "GITC-Uplift";
    public static readonly string[] UpliftFiles =
        [UpliftFile, UpliftHelper, Path.Combine(DfcShaderDir, "Uplift.fx"), Path.Combine(DfcShaderDir, "UpliftMask.fx")];
    internal static readonly string[] LegacyUpliftFiles = ["uplift.addon64", "uplift.addon32", "uplift-helper64.exe", "gitc-uplift.addon32"];

    /// <summary>Deep Fried Chicken (closed source, personal-use licence; import-only): the ReShade add-on and its NGX bridge.</summary>
    public const string DfcFile = "deep-fried-chicken.addon64";
    public const string DfcBridgeFile = "deep-fried-chicken-nvngx.dll";
    public const string DfcConfigFile = "deep-fried-chicken.cfg";
    /// <summary>From 4.x (INSTALL.txt, 64-bit route): beside the game exe like the add-on.</summary>
    public const string DfcPresentSupportFile = "deep-fried-chicken-present-support.dll";
    /// <summary>From 4.x: kept whole beside the game exe (64-bit) per INSTALL.txt.</summary>
    public const string DfcNeuralDir = "deep-fried-chicken-neural";
    /// <summary>DFC's own ReShade effects (DFC_OutputState.addonfx in 4.x) ship in reshade-shaders\Shaders.</summary>
    public const string DfcShaderDir = @"reshade-shaders\Shaders";
    public const string DfcShaderPrefix = "DFC_";
    /// <summary>The package's subfolder for 64-bit games; its 32-bit route keeps a companion set in host64 beside this exe.</summary>
    public const string DfcPackage64Dir = "64-bit";
    public const string DfcHost64Exe = "dfc-universal-host64.exe";
    /// <summary>The single files of the 64-bit set that go beside the game exe.</summary>
    public static readonly string[] DfcPackageFiles = [DfcFile, DfcBridgeFile, DfcPresentSupportFile];
    /// <summary>Files of DFC's own installer for the feeder route (non-DLSS games); this app never installs them, but they are NR injectors.</summary>
    public static readonly string[] DfcForeignFiles =
    [
        "dfc-native-d3d11.addon64", "dfc-native-d3d11.cfg",
        "dfc-universal-feed.addon32", "dfc-universal-feed.addon64", "dfc-universal-feed.cfg",
        "dlss5-feed.addon", "dlss5-feed.addon32", "dlss5-feed.addon64",
    ];
    /// <summary>Other NR injectors a RenoDX install moves out of the way (DFC refuses to coexist with the RenoDX add-ons).</summary>
    public static readonly string[] ForeignNrAddons = ["renodx-dlssfix.addon64", .. LecramFiles, .. DfcPackageFiles, .. DfcForeignFiles, .. UpliftFiles, .. LegacyUpliftFiles];
    /// <summary>The RenoDX family: what DFC moves out of the way.</summary>
    public static readonly string[] RenoDxFamily = [RenoDxFile, .. LecramFiles, .. LegacyRenoDxFiles];
    public static readonly string[] DlssFiles = ["nvngx_dlss.dll", "nvngx_dlssd.dll", "nvngx_dlssg.dll"];

    /// <summary>SHA-256 values published in the fork's INSTALL-DLSSNR.md.</summary>
    public static readonly Dictionary<string, string> KnownDlssNr = new(StringComparer.OrdinalIgnoreCase)
    {
        ["e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e"] = "NVIDIA original · RTX 50",
        ["e67dee209320cdafe0e93e45675d7aa34323a53acc57a72b2e40a181581c989a"] = "ShortFuse compat · RTX 20–50",
        ["2bc19bc09a7b89dd44f73e2ac0108fcc586e2154bdd0830f242f4f040e28789f"] = "NVIDIA original · RTX 50",
        ["cdfa6c1ec784867182164c7836f324f087b5c9f374d0c9fe46f54999feabf3ed"] = "ShortFuse compat · RTX 20–50 · CUDA 13.4.3",
    };

    private const string CompatHint = "Heavier on RTX 20/30: start with 1 pass and NR before upscaling.";
    private const string OriginalHint = "RTX 50 only.";

    /// <summary>Plain advice per known build, shown under the NR version picker.</summary>
    public static readonly Dictionary<string, string> KnownDlssNrHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e"] = OriginalHint,
        ["e67dee209320cdafe0e93e45675d7aa34323a53acc57a72b2e40a181581c989a"] = CompatHint,
        ["2bc19bc09a7b89dd44f73e2ac0108fcc586e2154bdd0830f242f4f040e28789f"] = OriginalHint,
        ["cdfa6c1ec784867182164c7836f324f087b5c9f374d0c9fe46f54999feabf3ed"] = CompatHint,
    };

    private readonly GitHubClient _gh;
    private readonly Func<bool> _includePrereleases;
    private readonly Dictionary<Component, SemaphoreSlim> _locks = new()
    {
        [Component.OptiScaler] = new(1, 1),
        [Component.MfgUnlock] = new(1, 1),
        [Component.Uplift] = new(1, 1),
        [Component.Dlss] = new(1, 1),
        [Component.Streamline] = new(1, 1),
        [Component.ReShade] = new(1, 1),
    };

    public ComponentStore(GitHubClient gh, Func<bool> includePrereleases)
    {
        _gh = gh;
        _includePrereleases = includePrereleases;
    }

    public ReleaseInfo? Opti { get; internal set; }
    public ReleaseInfo? Uplift { get; internal set; }
    public ReleaseInfo? Mfg { get; internal set; }
    public ReleaseInfo? Dlss { get; internal set; }
    public ReleaseInfo? Streamline { get; internal set; }
    /// <summary>Latest ReShade with full add-on support from reshade.me.</summary>
    public ReleaseInfo? ReShade { get; internal set; }
    /// <summary>Download ReShade automatically when none was imported.</summary>
    public Func<bool> AutoReShade { get; set; } = () => true;
    /// <summary>Every DLSS SDK release, newest first, for pinning an older version.</summary>
    public IReadOnlyList<ReleaseInfo> DlssReleases { get; internal set; } = [];

    public ReleaseInfo? Get(Component c) => c switch
    {
        Component.OptiScaler => Opti,
        Component.MfgUnlock => Mfg,
        Component.Uplift => Uplift,
        Component.Streamline => Streamline,
        Component.ReShade => ReShade,
        _ => Dlss,
    };

    public Func<string?> SelectedDlssNr { get; set; } = () => null;
    /// <summary>Repo of the chosen OptiScaler build (null = default).</summary>
    public Func<string?> OptiSourceRepo { get; set; } = () => null;
    public OptiSource OptiSource => OptiSources.For(OptiSourceRepo());
    public string DlssNrPath => DlssNrFor(SelectedDlssNr());
    /// <summary>Where 2.x kept a ReShade64.dll the user imported; <see cref="MigrateReShade"/> moves it into the ReShade library.</summary>
    public string ReShadePath => Path.Combine(AppPaths.Components, ReShadeFile);
    /// <summary>The user has imported a ReShade build (or still has the old single file).</summary>
    public bool ReShadeImported => File.Exists(ReShadePath) || ReShadeBuilds.Any(b => b.Channel == ReShadeChannel.Custom);

    /// <summary>The ReShade64.dll installs use right now: the default build's (null when the library has none).</summary>
    public string? CurrentReShadePath
    {
        get
        {
            if (File.Exists(ReShadePath)) return ReShadePath;
            return DefaultReShadeBuild() is { Has64: true } build ? ReShadeFilePath(build, 64) : null;
        }
    }

    // ---------- resolve ----------

    public async Task RefreshAsync(CancellationToken ct)
    {
        await Task.WhenAll(
            Resolve(Component.OptiScaler, ResolveOptiAsync, v => Opti = v, ct),
            Resolve(Component.Uplift, ResolveUpliftAsync, v => Uplift = v, ct),
            Resolve(Component.MfgUnlock, ResolveMfgAsync, v => Mfg = v, ct),
            Resolve(Component.Dlss, ResolveDlssAsync, v => Dlss = v, ct),
            Resolve(Component.Streamline, ResolveStreamlineAsync, v => Streamline = v, ct),
            Resolve(Component.ReShade, ResolveReShadeAsync, v => ReShade = v, ct));
        if (DlssReleases.Count == 0 && Dlss is not null) DlssReleases = [Dlss];
    }

    private async Task Resolve(Component c, Func<CancellationToken, Task<ReleaseInfo?>> resolve, Action<ReleaseInfo?> set, CancellationToken ct)
    {
        try
        {
            set(await resolve(ct) ?? Offline(c));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            var cached = Offline(c);
            Log.Info($"{c}: GitHub unreachable ({ex.Message}){(cached is null ? "" : $", using cached {cached.Tag}")}");
            set(cached);
        }
    }

    /// <summary>The newest downloaded release, with its notes from the API cache when GitHub had them.</summary>
    private ReleaseInfo? Offline(Component c)
    {
        var source = OptiSource;
        Func<ReleaseInfo, bool>? sameSource = c == Component.OptiScaler ? r => OptiSources.OfTag(r.Tag) == source : null;
        if (NewestCached(c, sameSource) is not { } cached) return null;
        var repo = c switch
        {
            Component.OptiScaler => source.Repo,
            Component.MfgUnlock => MfgRepo,
            Component.Uplift => UpliftRepo,
            Component.Dlss => DlssRepo,
            Component.Streamline => StreamlineRepo,
            _ => null,
        };
        var notes = repo is null ? null
            : _gh.CachedReleases(repo).FirstOrDefault(r => string.Equals(r.TagName, c == Component.OptiScaler ? OptiSources.RawTag(cached.Tag) : cached.Tag, StringComparison.OrdinalIgnoreCase))?.Body;
        return notes is null ? cached : new ReleaseInfo
        {
            Tag = cached.Tag, Prerelease = cached.Prerelease, Published = cached.Published, FromCache = true, Notes = notes,
        };
    }

    private bool Accept(GhRelease r) => !r.Draft && (!r.Prerelease || _includePrereleases());

    private async Task<ReleaseInfo?> ResolveOptiAsync(CancellationToken ct)
    {
        var source = OptiSource;
        foreach (var r in await _gh.GetReleasesAsync(source.Repo, 15, ct))
        {
            if (!Accept(r)) continue;
            var zip = r.Assets.FirstOrDefault(a => source.Asset.IsMatch(a.Name));
            if (zip is null) continue;
            return new ReleaseInfo
            {
                Tag = r.TagName + source.TagSuffix, Prerelease = r.Prerelease, Published = r.PublishedAt, Notes = r.Body, Stale = r.Stale,
                Files = [(zip.Name, zip.Url, zip.Sha256)],
            };
        }
        return null;
    }

    private async Task<ReleaseInfo?> ResolveMfgAsync(CancellationToken ct)
    {
        foreach (var r in await _gh.GetReleasesAsync(MfgRepo, 10, ct))
        {
            if (!Accept(r)) continue;
            var addon = r.Assets.FirstOrDefault(a => a.Name.Equals(MfgFile, StringComparison.OrdinalIgnoreCase));
            if (addon is null) continue;
            return new ReleaseInfo
            {
                Tag = r.TagName, Prerelease = r.Prerelease, Published = r.PublishedAt, Notes = r.Body, Stale = r.Stale,
                Files = [(MfgFile, addon.Url, addon.Sha256)],
            };
        }
        return null;
    }

    private async Task<ReleaseInfo?> ResolveDlssAsync(CancellationToken ct)
    {
        DlssReleases = (await _gh.GetReleasesAsync(DlssRepo, 40, ct))
            .Where(Accept)
            .Select(r => DlssRelease(r.TagName, r.Prerelease, r.PublishedAt, r.Body, r.Stale))
            .ToList();
        return DlssReleases.FirstOrDefault();
    }

    private static ReleaseInfo DlssRelease(string tag, bool pre = false, DateTime? published = null, string? notes = null, bool stale = false) => new()
    {
        Tag = tag, Prerelease = pre, Published = published, Notes = notes, Stale = stale,
        Files = DlssFiles.Select(f => (f, DlssRawUrl(tag, f), (string?)null)).ToList(),
    };

    /// <summary>The release to install for a pinned tag, or the latest when <paramref name="tag"/> is null.</summary>
    public ReleaseInfo? DlssFor(string? tag) =>
        tag is null ? Dlss : DlssReleases.FirstOrDefault(r => r.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) ?? DlssRelease(tag);

    [GeneratedRegex(@"^streamline-sdk-v[\d.]+\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex StreamlineAsset();

    private async Task<ReleaseInfo?> ResolveStreamlineAsync(CancellationToken ct)
    {
        foreach (var r in await _gh.GetReleasesAsync(StreamlineRepo, 10, ct))
        {
            if (!Accept(r)) continue;
            var zip = r.Assets.FirstOrDefault(a => StreamlineAsset().IsMatch(a.Name));
            if (zip is null) continue;
            return new ReleaseInfo
            {
                Tag = r.TagName, Prerelease = r.Prerelease, Published = r.PublishedAt, Notes = r.Body, Stale = r.Stale,
                Files = [(zip.Name, zip.Url, zip.Sha256)],
            };
        }
        return null;
    }

    [GeneratedRegex(@"downloads/ReShade_Setup_(\d+(?:\.\d+)+)_Addon\.exe", RegexOptions.IgnoreCase)]
    private static partial Regex ReShadeDownload();

    internal static string ReShadeUrl(string version) => $"{ReShadeSite}/downloads/ReShade_Setup_{version}_Addon.exe";

    /// <summary>Newest version linked as an add-on setup on a reshade.me page, or null.</summary>
    internal static string? ParseReShadePage(string html) =>
        ReShadeDownload().Matches(html).Select(m => m.Groups[1].Value)
            .OrderByDescending(FileUtil.ParseTag).FirstOrDefault();

    /// <summary>
    /// reshade.me links the current add-on build on its front page; crosire/reshade's newest tag is the fallback
    /// when the page layout changes. Both resolve to the official setup download.
    /// </summary>
    private async Task<ReleaseInfo?> ResolveReShadeAsync(CancellationToken ct)
    {
        string? version = null;
        try
        {
            var (addon, plain) = ReShadeSource.ParseSitePage(await _gh.GetStringAsync(ReShadeSite, ct));
            version = addon;
            ReShadeNoAddonsVersion = plain ?? ReShadeNoAddonsVersion;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
        { Log.Info($"reshade.me unreachable ({ex.Message}), trying GitHub tags"); }

        version ??= (await _gh.GetTagsAsync(ReShadeRepo, 10, ct))
            .Select(t => t.TrimStart('v', 'V'))
            .Where(t => FileUtil.ParseTag(t) is not null)
            .OrderByDescending(FileUtil.ParseTag)
            .FirstOrDefault();
        return version is null ? null : new ReleaseInfo { Tag = version, Files = [($"ReShade_Setup_{version}_Addon.exe", ReShadeUrl(version), null)] };
    }

    private static string DlssRawUrl(string refName, string file) =>
        $"https://raw.githubusercontent.com/{DlssRepo}/{refName}/lib/Windows_x86_64/rel/{file}";

    // ---------- cache ----------

    /// <summary>OptiScaler and GITC Uplift downloads are kept as a build library under components; the rest is cache.</summary>
    private static string ComponentDir(Component c) =>
        Path.Combine(c is Component.OptiScaler or Component.Uplift ? AppPaths.Components : AppPaths.Cache, c.ToString().ToLowerInvariant());
    internal static string TagDir(Component c, string tag) => Path.Combine(ComponentDir(c), SafeName(tag));
    private static string SafeName(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    private const string CompleteMarker = ".complete";

    public static bool IsCached(Component c, string tag) => File.Exists(Path.Combine(TagDir(c, tag), CompleteMarker));

    /// <summary>
    /// The complete builds under one component's folder with their .complete records: one folder per tag, never the
    /// rest of a build the cleanup is deleting. An unreadable record is logged and skipped.
    /// </summary>
    private static List<(string Dir, CachedComponent Meta)> CompleteBuilds(Component c, string? root = null)
    {
        root ??= ComponentDir(c);
        var found = new List<(string, CachedComponent)>();
        if (!Directory.Exists(root)) return found;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var marker = Path.Combine(dir, CompleteMarker);
            if (dir.EndsWith(DeletingSuffix, StringComparison.OrdinalIgnoreCase) || !File.Exists(marker)) continue;
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllText(marker), JsonCtx.Default.CachedComponent) is { } meta && !string.IsNullOrWhiteSpace(meta.Tag))
                    found.Add((dir, meta));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            { Log.Error($"Could not read cached component {marker}", ex); }
        }
        return found;
    }

    internal static ReleaseInfo? NewestCached(Component c, Func<ReleaseInfo, bool>? filter = null)
    {
        var releases = CompleteBuilds(c).Select(b => (Release: new ReleaseInfo
        {
            Tag = b.Meta.Tag, Prerelease = b.Meta.Prerelease, Published = b.Meta.Published, FromCache = true,
        }, Date: b.Meta.Published ?? b.Meta.Fetched)).Where(r => filter is null || filter(r.Release));
        return BuildOrder.NewestFirst(releases, r => c == Component.OptiScaler ? OptiSources.RawTag(r.Release.Tag) : r.Release.Tag, r => r.Date, r => r.Release.Tag)
            .Select(r => r.Release).FirstOrDefault();
    }

    internal static void MarkComplete(Component c, ReleaseInfo r)
    {
        var meta = new CachedComponent { Tag = r.Tag, Prerelease = r.Prerelease, Published = r.Published, Fetched = DateTime.UtcNow };
        FileUtil.AtomicWriteText(Path.Combine(TagDir(c, r.Tag), CompleteMarker), JsonSerializer.Serialize(meta, JsonCtx.Default.CachedComponent));
        ReleasesChanged();
    }

    // ---------- ensure (download + verify) ----------

    /// <summary>
    /// Returns the folder holding the extracted OptiScaler package. <paramref name="tag"/> null means the newest
    /// release; another tag must already be cached.
    /// </summary>
    public async Task<string> EnsureOptiAsync(string? tag, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var name = (tag is null ? OptiSource : OptiSources.OfTag(tag)).Name;
        var r = CachedOrNewest(Component.OptiScaler, tag, Opti, name)
                ?? throw new InvalidOperationException($"{name} release unknown (offline and nothing cached).");
        var dir = TagDir(Component.OptiScaler, r.Tag);
        var pkg = Path.Combine(dir, "pkg");
        await _locks[Component.OptiScaler].WaitAsync(ct);
        try
        {
            if (IsCached(Component.OptiScaler, r.Tag)) return pkg;
            if (r.FromCache) throw new InvalidOperationException("Cached OptiScaler package is incomplete.");

            var (file, url, sha) = r.Files[0];
            var zip = Path.Combine(dir, file);
            await _gh.DownloadAsync(url, zip, $"{name} {r.Tag}", progress, ct);
            progress?.Report(new TransferProgress($"Verifying {name}", null));
            await VerifyAsync(zip, sha, ct);

            progress?.Report(new TransferProgress($"Extracting {name}", null));
            if (Directory.Exists(pkg)) Directory.Delete(pkg, true);
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, pkg, true), ct);
            File.Delete(zip);
            if (!File.Exists(Path.Combine(pkg, "OptiScaler.dll")))
                throw new InvalidDataException("OptiScaler.dll missing from release package.");

            MarkComplete(Component.OptiScaler, r);
            NoteAdded(r.Tag);
            Log.Info($"Cached {name} {r.Tag}");
            return pkg;
        }
        finally { _locks[Component.OptiScaler].Release(); }
    }

    /// <summary>
    /// Returns the path of the MFG Unlock addon. <paramref name="tag"/> null means the newest release, "local" the
    /// imported file; another tag must already be cached.
    /// </summary>
    public async Task<string> EnsureMfgAsync(string? tag, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var local = Path.Combine(AppPaths.Components, MfgFile);
        if (tag == LocalMfgTag)
            return File.Exists(local) ? local : throw new FileNotFoundException("The imported MFG Unlock add-on is gone.", local);
        if (tag is null && Mfg is null && File.Exists(local)) return local;
        var r = CachedOrNewest(Component.MfgUnlock, tag, Mfg, "MFG Unlock")
                ?? throw new InvalidOperationException("MFG Unlock release unknown (offline and nothing cached).");
        var dir = TagDir(Component.MfgUnlock, r.Tag);
        var file = Path.Combine(dir, MfgFile);
        await _locks[Component.MfgUnlock].WaitAsync(ct);
        try
        {
            if (IsCached(Component.MfgUnlock, r.Tag)) return file;
            if (r.FromCache) throw new InvalidOperationException("Cached MFG Unlock is incomplete.");

            var (_, url, sha) = r.Files[0];
            await _gh.DownloadAsync(url, file, $"MFG Unlock {r.Tag}", progress, ct);
            await VerifyAsync(file, sha, ct);
            MarkComplete(Component.MfgUnlock, r);
            Log.Info($"Cached MFG Unlock {r.Tag}");
            return file;
        }
        finally { _locks[Component.MfgUnlock].Release(); }
    }

    /// <summary>Tag recorded for an MFG Unlock add-on the user imported instead of a release.</summary>
    public const string LocalMfgTag = "local";

    /// <summary>True when that MFG Unlock version can be installed without a download.</summary>
    public static bool HasMfg(string tag) =>
        tag == LocalMfgTag ? File.Exists(Path.Combine(AppPaths.Components, MfgFile)) : IsCached(Component.MfgUnlock, tag);

    private static ReleaseInfo? CachedOrNewest(Component c, string? tag, ReleaseInfo? newest, string label)
    {
        if (tag is null || (newest is not null && newest.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase))) return newest;
        return IsCached(c, tag)
            ? new ReleaseInfo { Tag = tag, FromCache = true }
            : throw new InvalidOperationException($"{label} {tag} is no longer cached.");
    }

    /// <summary>
    /// Returns file name -> cached path for the nvngx_dlss / dlssd / dlssg files the release has
    /// (older SDKs ship without RR or FG). <paramref name="tag"/> null means the latest release.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> EnsureDlssAsync(string? tag, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var r = DlssFor(tag) ?? throw new InvalidOperationException("DLSS release unknown (offline and nothing cached).");
        var dir = TagDir(Component.Dlss, r.Tag);
        IReadOnlyDictionary<string, string> Present() => DlssFiles
            .Select(f => (Name: f, Path: Path.Combine(dir, f)))
            .Where(x => File.Exists(x.Path))
            .ToDictionary(x => x.Name, x => x.Path, StringComparer.OrdinalIgnoreCase);

        await _locks[Component.Dlss].WaitAsync(ct);
        try
        {
            if (IsCached(Component.Dlss, r.Tag)) return Present();
            if (r.FromCache) throw new InvalidOperationException("Cached DLSS files are incomplete.");

            var expected = r.Version;
            var latest = ReferenceEquals(r, Dlss);
            foreach (var (name, url, _) in r.Files)
            {
                var src = url;
                if (!await _gh.ExistsAsync(src, ct))
                {
                    if (!latest) continue; // this SDK version has no such file
                    src = DlssRawUrl("main", name);
                }
                var dest = Path.Combine(dir, name);
                await _gh.DownloadAsync(src, dest, $"{name} {r.Tag}", progress, ct);

                var v = FileUtil.ReadVersion(dest) ?? throw new InvalidDataException($"{name}: not a valid DLL.");
                if (expected is not null && (v.Major != expected.Major || v.Minor != expected.Minor))
                    Log.Info($"{name}: file version {FileUtil.Format(v)} differs from tag {r.Tag}");
            }
            if (!File.Exists(Path.Combine(dir, DlssFiles[0])))
                throw new InvalidDataException($"DLSS {r.Tag}: nvngx_dlss.dll not found in the SDK.");
            MarkComplete(Component.Dlss, r);
            Log.Info($"Cached DLSS {r.Tag}");
            return Present();
        }
        finally { _locks[Component.Dlss].Release(); }
    }

    /// <summary>Returns the folder with the signed production sl.*.dll files (bin/x64 of the SDK zip).</summary>
    public async Task<string> EnsureStreamlineAsync(IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var r = Streamline ?? throw new InvalidOperationException("Streamline release unknown (offline and nothing cached).");
        var dir = TagDir(Component.Streamline, r.Tag);
        var bin = Path.Combine(dir, "x64");
        await _locks[Component.Streamline].WaitAsync(ct);
        try
        {
            if (IsCached(Component.Streamline, r.Tag)) return bin;
            if (r.FromCache) throw new InvalidOperationException("Cached Streamline files are incomplete.");

            var (name, url, sha) = r.Files[0];
            var zip = Path.Combine(dir, name);
            await _gh.DownloadAsync(url, zip, $"Streamline {r.Tag}", progress, ct);
            progress?.Report(new TransferProgress("Verifying Streamline", null));
            await VerifyAsync(zip, sha, ct);

            progress?.Report(new TransferProgress("Extracting Streamline", null));
            await Task.Run(() =>
            {
                Directory.CreateDirectory(bin);
                using var archive = ZipFile.OpenRead(zip);
                foreach (var e in archive.Entries)
                {
                    // Signed production runtime only; bin/x64/development holds unsigned debug builds.
                    var n = e.FullName.Replace('\\', '/');
                    if (!n.StartsWith("bin/x64/", StringComparison.OrdinalIgnoreCase) || n.Count(c => c == '/') != 2) continue;
                    if (!e.Name.StartsWith("sl.", StringComparison.OrdinalIgnoreCase) || !e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                    e.ExtractToFile(Path.Combine(bin, e.Name), true);
                }
            }, ct);
            File.Delete(zip);
            if (!File.Exists(Path.Combine(bin, "sl.interposer.dll")))
                throw new InvalidDataException("sl.interposer.dll missing from the Streamline SDK.");

            MarkComplete(Component.Streamline, r);
            Log.Info($"Cached Streamline {r.Tag}");
            return bin;
        }
        finally { _locks[Component.Streamline].Release(); }
    }

    /// <summary>ReShade's setup is an exe with a zip appended; ReShade64.dll is one of its entries.</summary>
    internal static void ExtractReShade(string setupExe, string destDll)
    {
        var bytes = File.ReadAllBytes(setupExe);
        var start = AppendedZipStart(bytes);
        using var archive = new ZipArchive(new MemoryStream(bytes, start, bytes.Length - start, writable: false), ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(ReShadeFile, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("ReShade64.dll missing from the ReShade setup.");
        Directory.CreateDirectory(Path.GetDirectoryName(destDll)!);
        entry.ExtractToFile(destDll, true);
    }

    /// <summary>
    /// Where a zip appended to other data begins. The archive's offsets are relative to its own start, which
    /// ZipArchive can't handle with the exe in front: the real central directory sits right before the
    /// end-of-central-directory record, so start = that position minus the offset the record claims.
    /// </summary>
    internal static int AppendedZipStart(byte[] data)
    {
        for (var i = data.Length - 22; i >= Math.Max(0, data.Length - 22 - 65535); i--)
        {
            if (data[i] != 0x50 || data[i + 1] != 0x4B || data[i + 2] != 0x05 || data[i + 3] != 0x06) continue;
            var cdSize = BitConverter.ToUInt32(data, i + 12);
            var cdOffset = BitConverter.ToUInt32(data, i + 16);
            var start = (long)i - cdSize - cdOffset;
            if (start >= 0) return (int)start;
        }
        throw new InvalidDataException("No zip archive found in the ReShade setup.");
    }

    /// <summary>Checks a GitHub release asset against the digest GitHub published for it; no digest is a failure.</summary>
    private static async Task VerifyAsync(string file, string? sha256, CancellationToken ct)
    {
        if (sha256 is null)
        {
            File.Delete(file);
            throw new InvalidDataException($"{Path.GetFileName(file)}: GitHub published no SHA-256 digest for it, so it can't be verified.");
        }
        var actual = await Task.Run(() => FileUtil.Sha256(file), ct);
        if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file);
            throw new InvalidDataException($"{Path.GetFileName(file)}: SHA-256 mismatch.");
        }
    }

    public static bool IsDownloaded(ReleaseInfo? r, Component c) => r is not null && IsCached(c, r.Tag);

    // ---------- user-supplied components ----------

    public void Import(string source, string fileName)
    {
        if (fileName == DlssNrFile) { ImportDlssNr(source); return; }
        if (fileName == RenoDxFile) { ImportRenoDx(source); return; }
        if (LecramFiles.Contains(fileName)) { ImportLecram(source); return; }
        if (fileName == DfcFile) { ImportDfc(source); return; }
        if (fileName == ReShadeFile) { ImportReShade(source); return; }
        if (fileName != MfgFile) throw new ArgumentException("Unsupported component.", nameof(fileName));
        Directory.CreateDirectory(AppPaths.Components);
        FileUtil.AtomicCopy(source, Path.Combine(AppPaths.Components, fileName));
        Log.Info($"Imported {fileName} from {source}");
    }

    /// <summary>First run: pick up nvngx_dlssnr.dll / ReShade64.dll lying next to the exe.</summary>
    public void AutoImport()
    {
        MigrateDlssNr();
        MigrateAddons();
        MigrateDfc();
        MigrateReShade();
        MigrateReleaseLibraries();
        var dirs = new[] { Path.GetDirectoryName(Environment.ProcessPath), AppContext.BaseDirectory, Environment.CurrentDirectory }
            .Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new[] { DlssNrFile, ReShadeFile, MfgFile, RenoDxFile, DfcFile })
        {
            if (File.Exists(Path.Combine(AppPaths.Components, file)) || (file == DlssNrFile && DlssNrVersions.Count > 0)
                || (file == RenoDxFile && RenoDxImported) || (file == DfcFile && DfcImported) || (file == ReShadeFile && ReShadeImported)) continue;
            var src = dirs.Select(d => Path.Combine(d!, file))
                .FirstOrDefault(f => File.Exists(f) && (file != DfcFile || File.Exists(Path.Combine(Path.GetDirectoryName(f)!, DfcBridgeFile))));
            if (src is null) continue;
            try { Import(src, file); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { Log.Error($"Auto-import of {file} failed", ex); }
        }
    }
}
