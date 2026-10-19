using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>OptiScaler-NR loads ReShade (default), or ReShade alone is the proxy and loads the add-ons.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InstallMode>))]
public enum InstallMode { OptiScaler, ReShadeOnly }

public sealed record InstallOptions
{
    public InstallMode Mode { get; init; }
    public bool ReShadeOnly => Mode == InstallMode.ReShadeOnly;
    public bool Opti { get; init; }
    public bool ReShade { get; init; }
    public bool Mfg { get; init; }
    public bool DlssNr { get; init; }
    public string? DlssNrId { get; init; }
    public bool RenoDx { get; init; }
    /// <summary>ShortFuse add-on build from the library; null = the default build.</summary>
    public string? RenoDxId { get; init; }
    /// <summary>Lecram's DLSS5 add-on (imported); excludes <see cref="Dfc"/> and OptiScaler-NR, but may run next to <see cref="RenoDx"/>.</summary>
    public bool Lecram { get; init; }
    /// <summary>Lecram add-on build from the library (it decides v10 or v11 placement); null = the default build.</summary>
    public string? LecramId { get; init; }
    /// <summary>GITC Uplift, a downloaded ReShade add-on; excludes every other NR route.</summary>
    public bool Uplift { get; init; }
    public string? UpliftTag { get; init; }
    /// <summary>Deep Fried Chicken, an imported ReShade add-on; excludes <see cref="RenoDx"/> (two NR injectors).</summary>
    public bool Dfc { get; init; }
    public bool Dlss { get; init; }
    /// <summary>Put nvngx_dlss.dll next to OptiScaler when the game has none (independent of <see cref="Dlss"/>).</summary>
    public bool AddMissingDlss { get; init; }
    /// <summary>
    /// With <see cref="Dfc"/> in a game without its own DLSS: also put NVIDIA's nvngx_dlssg.dll beside the exe for DFC's
    /// DLSS-G frame generation (INSTALL.txt 4.0.6). Ignored in games with native DLSS.
    /// </summary>
    public bool DfcFrameGen { get; init; }
    /// <summary>DLSS release to install; null = latest (never downgrades a newer game DLL).</summary>
    public string? DlssTag { get; init; }
    public bool Streamline { get; init; }
    /// <summary>False = each installed mod starts from its defaults plus app settings (old configs backed up).</summary>
    public bool CarryOverIni { get; init; } = true;
    /// <summary>App settings replace values the game's config already has.</summary>
    public bool OverwriteIni { get; init; }
    public string Proxy { get; init; } = "dxgi.dll";
    public IReadOnlyList<IniOverride> Overrides { get; init; } = [];
    public IReadOnlyList<IniOverride> ReShadeOverrides { get; init; } = [];
    public bool AntiCheatConfirmed { get; init; }
    /// <summary>OptiScaler-NR release to install from the cache; null = the newest.</summary>
    public string? OptiTag { get; init; }
    /// <summary>MFG Unlock release to install from the cache ("local" = the imported file); null = the newest.</summary>
    public string? MfgTag { get; init; }
    /// <summary>MFG Unlock is off here only because the installed release left the cache: the add-on in the game stays as it is.</summary>
    public bool KeepMfg { get; init; }

    /// <summary>
    /// The same install with OptiScaler-NR and MFG Unlock held at the versions the game already has, for automatic
    /// reinstalls that must not pull in a release nobody chose. A component whose installed
    /// version is no longer cached is left as it is.
    /// </summary>
    public InstallOptions KeepingInstalledVersions(InstallManifest m) => this with
    {
        Opti = Opti && m.OptiTag is { } ot && ComponentStore.IsCached(Component.OptiScaler, ot),
        OptiTag = m.OptiTag,
        Mfg = Mfg && m.MfgTag is { } mt && ComponentStore.HasMfg(mt),
        KeepMfg = Mfg && !(m.MfgTag is { } kt && ComponentStore.HasMfg(kt)),
        MfgTag = m.MfgTag,
        UpliftTag = m.UpliftTag,
        DlssNrId = m.DlssNrId,
        RenoDxId = m.RenoDxId ?? RenoDxId,
        LecramId = m.LecramId ?? LecramId,
    };
}

public sealed class NeedsAdminException(string path)
    : Exception($"Windows blocked access to {path}. Close the game and any program using its files (overlays, antivirus scans), then try again. " +
                "If the game is in a protected folder, restart DLSS Updater as administrator.");

public sealed class GameFileInUseException(string gameName, Exception inner)
    : IOException($"A game file is in use. Close {gameName} and retry.", inner);

public sealed class GameRunningException(string exe)
    : Exception($"{Path.GetFileName(exe)} is running. Close the game first.");

public sealed class Installer(ComponentStore store)
{
    private static readonly string[] LegacyFiles =
        ["OptiScaler.asi", "nvngx.dll_dlssnr.dll", "Remove OptiScaler.bat", "Remove_OptiScaler.bat", "OptiScaler.dll"];

    private static readonly string[] PackageDirs = ["OptiScaler", "Licenses"];
    private static readonly string[] LogFiles = ["OptiScaler.log", "ReShade.log", "ReShade64.log"];

    private sealed record Ctx(string Root, string Target, InstallManifest M)
    {
        public string Rel(string full) => InstallManifest.Entry(full, Target, Root);
        /// <summary>Entries live in the game or in the install folder, which the user may have picked outside the game.</summary>
        public string Full(string rel)
        {
            var full = InstallManifest.Resolve(rel, Target, Root);
            return FileUtil.IsUnder(full, Root) || FileUtil.IsUnder(full, Target)
                ? full
                : throw new UnsafeManifestException(Target, $"\"{rel}\" is outside {Root} and {Target}");
        }
        public string BackupDir => InstallManifest.BackupDirFor(Target);
    }

    // ---------- install / update ----------

    public async Task InstallAsync(GameInfo game, string targetDir, InstallOptions o, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (Demo.Active)
        {
            await Demo.InstallAsync(store, game, targetDir, o, progress, ct);
            return;
        }
        targetDir = FileUtil.Normalize(targetDir);
        EnsureNotRunning(game.Root);
        EnsureWritable(targetDir);
        try
        {
            await InstallCoreAsync(game, targetDir, o, progress, ct);
        }
        finally
        {
            FileUtil.TryDeleteEmptyDir(InstallManifest.DirFor(targetDir));
        }
    }

    private async Task InstallCoreAsync(GameInfo game, string targetDir, InstallOptions o, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (o.Uplift)
        {
            if (!o.ReShadeOnly || o.Opti || o.RenoDx || o.Lecram || o.Dfc)
                throw new InvalidOperationException("GITC Uplift replaces OptiScaler-NR, ShortFuse, Lecram and Deep Fried Chicken; pick one route.");
            if (!o.DlssNr) throw new InvalidOperationException("GITC Uplift needs an imported NR runtime.");
            o = o with { ReShade = true };
            var exe = game.Exes.FirstOrDefault(f => string.Equals(Path.GetDirectoryName(f), targetDir, StringComparison.OrdinalIgnoreCase))
                ?? game.Exes.FirstOrDefault()
                ?? throw new InvalidOperationException("GITC Uplift needs a game executable to verify that the game is 64-bit x64.");
            {
                using var stream = File.OpenRead(exe);
                using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
                if (pe.PEHeaders.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.Amd64)
                    throw new InvalidOperationException("GITC Uplift supports 64-bit x64 games only in this app.");
            }
        }
        if (o.ReShadeOnly && !o.ReShade) throw new InvalidOperationException("ReShade + add-ons mode installs ReShade as the proxy; ReShade can't be left out.");
        if (!AppSettings.IsKnownProxy(o.Proxy)) throw new InvalidOperationException($"\"{o.Proxy}\" is not a proxy name DLSS Updater installs under.");
        EnsureNoDoubleMfgUnlock(targetDir, o);
        // Fetch everything first so a network failure never leaves a half-installed game.
        var pkg = o.Opti && !o.ReShadeOnly ? await store.EnsureOptiAsync(o.OptiTag, progress, ct) : null;
        var dlssNr = o.DlssNr;
        var nrPath = dlssNr ? store.VerifiedDlssNrPath(o.DlssNrId ?? store.SelectedDlssNr()) : null;
        if (o.RenoDx && !o.ReShadeOnly)
            throw new InvalidOperationException("The RenoDX DLSS add-on only works in ReShade + add-ons mode: with OptiScaler-NR there would be two NR injectors.");
        if (o.RenoDx && (!o.ReShade || !dlssNr))
            throw new InvalidOperationException("RenoDX needs ReShade with add-on support and an imported NR runtime.");
        // DLSS files an earlier install of ours added (for DFC or OptiScaler) don't make the game a DLSS game.
        var nativeDlss = (o.RenoDx || o.Lecram || o.Dfc) && HasNativeDlss(game, InstallManifest.Read(targetDir), targetDir);
        if (o.RenoDx && !nativeDlss)
            throw new InvalidOperationException("RenoDX needs a game with native DLSS.");
        if (o.Lecram && !o.ReShadeOnly)
            throw new InvalidOperationException("Lecram's DLSS5 add-on only works in ReShade + add-ons mode: with OptiScaler-NR there would be two NR injectors.");
        if (o.Lecram && (!o.ReShade || !dlssNr))
            throw new InvalidOperationException("Lecram's DLSS5 add-on needs ReShade with add-on support and an imported NR runtime.");
        if (o.Lecram && !nativeDlss)
            throw new InvalidOperationException("Lecram's DLSS5 add-on needs a game with native DLSS.");
        if (o.Dfc && o.RenoDx)
            throw new InvalidOperationException("Deep Fried Chicken and the RenoDX DLSS add-on can't run together: pick one.");
        if (o.Dfc && o.Lecram)
            throw new InvalidOperationException("Deep Fried Chicken and Lecram's DLSS5 add-on can't run together: pick one.");
        if (o.Dfc && !o.ReShadeOnly)
            throw new InvalidOperationException("Deep Fried Chicken only works in ReShade + add-ons mode: with OptiScaler-NR there would be two NR injectors.");
        if (o.Dfc && (!o.ReShade || !dlssNr))
            throw new InvalidOperationException("Deep Fried Chicken needs ReShade with add-on support and an imported NR runtime.");
        var renoDx = o.RenoDx ? store.RequireAddon(AddonKind.RenoDx, o.RenoDxId) : null;
        var lecram = o.Lecram ? store.RequireAddon(AddonKind.Lecram, o.LecramId) : null;
        var dfc = o.Dfc ? store.RequireDfc() : null;
        if (dfc is not null && !nativeDlss && !store.DfcSupportsNonDlssGames)
            throw new InvalidOperationException(DfcNeedsNativeDlss(store.DfcVersion));
        // INSTALL.txt: add your own nvngx_dlss.dll beside the game exe; DFC does not ship it. The game has none of its own here.
        var dfcRuntimes = dfc is not null && !nativeDlss;
        var uplift = o.Uplift ? await store.EnsureUpliftAsync(o.UpliftTag, progress, ct) : null;
        if (o.Uplift && UpliftConfig.Warning(o.UpliftTag ?? store.Uplift?.Tag) is { } upliftWarning) Log.Warn($"{game.Name}: {upliftWarning}");
        var addMissing = o.AddMissingDlss && o.Opti;
        var dlss = o.Dlss || addMissing || dfcRuntimes ? await store.EnsureDlssAsync(o.DlssTag, progress, ct) : null;
        var mfgPlan = o.Mfg ? PlanMfg(game, o, dlss, FileUtil.ParseTag(store.Streamline?.Tag)) : null;
        if (mfgPlan?.Skip is { } skip) Log.Info($"{game.Name}: MFG Unlock skipped. {skip}");
        foreach (var w in mfgPlan?.Warnings ?? []) Log.Warn($"{game.Name}: {w}");
        if (mfgPlan?.SwapStreamline == true) Log.Info($"{game.Name}: Dynamic MFG needs the matching Streamline set, so the game's sl.*.dll files are replaced too");
        var mfg = mfgPlan?.Install == true ? await store.EnsureMfgAsync(o.MfgTag, progress, ct) : null;
        var swapSl = o.Streamline || mfgPlan?.SwapStreamline == true;
        var sl = swapSl && game.Streamline.Count > 0 ? await store.EnsureStreamlineAsync(progress, ct) : null;
        if (o.Streamline && game.Streamline.Count == 0) Log.Info($"{game.Name}: no Streamline files in this game, skipped");
        var reshade = o.ReShade ? await store.EnsureReShadeFileAsync(progress, ct) : null;
        if (o.RenoDx && (reshade is null || FileUtil.ReadVersion(reshade) is not { Major: >= 6 }))
            throw new InvalidOperationException("RenoDX needs ReShade 6 or newer with full add-on support.");
        if (o.Lecram && (reshade is null || FileUtil.ReadVersion(reshade) is not { Major: >= 6 }))
            throw new InvalidOperationException("Lecram's DLSS5 add-on needs ReShade 6 or newer with full add-on support.");
        if (o.Dfc && (reshade is null || FileUtil.ReadVersion(reshade) is not { Major: >= 6 }))
            throw new InvalidOperationException("Deep Fried Chicken needs ReShade 6 or newer with full add-on support.");
        if (o.Uplift && (reshade is null || FileUtil.ReadVersion(reshade) is not { Major: >= 6 }))
            throw new InvalidOperationException("GITC Uplift needs ReShade 6 or newer with full add-on support (6.8+ for Vulkan/OpenGL).");
        if (o.Uplift && o.Proxy is "opengl32.dll" && FileUtil.ReadVersion(reshade!) < new Version(6, 8))
            throw new InvalidOperationException("GITC Uplift on OpenGL needs ReShade 6.8 or newer.");
        if (store.SfLecramMessage(o.RenoDx, o.Lecram, renoDx?.Build.Id, lecram?.Build.Id) is { } compat)
        {
            if (store.PairTestFor(renoDx?.Build.Id, lecram?.Build.Id)?.Status == AddonPairStatus.Crashed) Log.Warn($"{game.Name}: {compat}");
            else Log.Info($"{game.Name}: {compat}");
        }

        progress?.Report(new TransferProgress($"Installing to {game.Name}", null));
        // An unreadable or unsafe record stops the install instead of starting over and burying the originals.
        var m = InstallManifest.Read(targetDir) ?? new InstallManifest();
        m.Contain(targetDir, game.Root);
        var ctx = new Ctx(FileUtil.Normalize(game.Root), targetDir, m);
        if (o.Uplift)
            foreach (var full in ComponentStore.UpliftFiles.Select(f => Path.Combine(targetDir, f)).Append(UpliftConfig.NrPath(targetDir)))
                if (FileUtil.LinkBelow(FileUtil.IsUnder(full, ctx.Root) ? ctx.Root : ctx.Target, full) is { } link)
                    throw new UnsafeManifestException(targetDir, $"{link} is a link");
        if (m.Uplift && !o.Uplift && (o.ReShade || pkg is not null) && nrPath is null && m.DlssNr)
            nrPath = store.VerifiedDlssNrPath(m.DlssNrId);

        await Task.Run(() =>
        {
            try
            {
                // Save fresh mode's original before route changes can clean up any add-on keys.
                var reShadeIni = Path.Combine(targetDir, "ReShade.ini");
                if (!o.CarryOverIni && reshade is not null && File.Exists(reShadeIni)
                    && !m.Owns(ctx.Rel(reShadeIni)) && m.BackupOf(ctx.Rel(reShadeIni)) is null)
                    Backup(ctx, reShadeIni, "file", copy: true);

                if (o.ReShadeOnly) RemoveOpti(ctx, o.Proxy, keepNr: dlssNr);
                else if (m.Mode == InstallMode.ReShadeOnly && (pkg is not null || reshade is not null))
                {
                    // Without OptiScaler taking over the proxy name, ReShade's proxy goes whatever its name: ReShade64.dll waits for a loader.
                    RemoveReShadeProxy(ctx, pkg is not null ? o.Proxy : null);
                    if (pkg is null) m.Proxy = null;
                    if (reshade is null) { m.ReShade = false; m.ReShadeSha = null; }
                }
                // One proxy per install: a new name takes ours out under the old one and puts back what it replaced.
                if ((o.ReShadeOnly || pkg is not null) && m.Proxy is { } oldProxy && !oldProxy.Equals(o.Proxy, StringComparison.OrdinalIgnoreCase))
                    RemoveOwned(ctx, Path.Combine(targetDir, oldProxy));
                if (o.ReShadeOnly || pkg is not null || reshade is not null) m.Mode = o.Mode;

                // Only a run that sets up the injectors drops the add-on; a DLSS-only update keeps it.
                if (!o.RenoDx && m.RenoDx && (o.ReShade || pkg is not null)) RemoveRenoDx(ctx);
                if (!o.Lecram && m.Lecram && (o.ReShade || pkg is not null)) RemoveLecram(ctx);
                if (!o.Dfc && m.Dfc && (o.ReShade || pkg is not null)) RemoveDfc(ctx);
                if (!o.Uplift && m.Uplift && (o.ReShade || pkg is not null)) RemoveUplift(ctx);
                if (!o.Mfg && !o.KeepMfg && m.Mfg && (o.ReShade || pkg is not null)) RemoveMfg(ctx);
                if (!o.ReShade && m.ReShade && !o.ReShadeOnly && pkg is not null) RemoveReShade(ctx);

                if (pkg is not null)
                {
                    InstallOpti(ctx, pkg, o, OptiSources.OfTag(o.OptiTag ?? store.Opti?.Tag));
                    if (dlssNr || m.DlssNr) DisplaceForeignNrAddons(ctx, [ComponentStore.RenoDxFile, .. ComponentStore.LecramFiles, .. ComponentStore.DfcPackageFiles, .. ComponentStore.DfcForeignFiles, .. ComponentStore.UpliftFiles, .. ComponentStore.LegacyUpliftFiles]);
                    m.Opti = true;
                    m.OptiTag = o.OptiTag ?? store.Opti?.Tag;
                    m.Proxy = o.Proxy;
                }
                if (reshade is not null)
                {
                    WriteReShadeIni(ctx, o, addonEffects: uplift is not null || (dfc is not null && store.DfcInstallFiles().Any(f => f.StartsWith(ComponentStore.DfcShaderDir + "\\", StringComparison.OrdinalIgnoreCase))),
                        lecramGen: lecram?.Build.Generation);
                    // ReShade-only: ReShade itself is the proxy the game loads; otherwise OptiScaler loads ReShade64.dll.
                    Place(ctx, reshade, Path.Combine(targetDir, o.ReShadeOnly ? o.Proxy : ComponentStore.ReShadeFile));
                    if (o.ReShadeOnly)
                    {
                        m.Proxy = o.Proxy;
                        Log.Info($"  {o.Proxy} ← ReShade");
                    }
                    m.ReShade = true;
                    m.ReShadeSha = HashCache.Get(reshade);
                }
                if (mfg is not null)
                {
                    Place(ctx, mfg, Path.Combine(targetDir, ComponentStore.MfgFile));
                    m.Mfg = true;
                    m.MfgTag = o.MfgTag ?? store.Mfg?.Tag ?? ComponentStore.LocalMfgTag;
                }
                if (renoDx is not null)
                {
                    // Lecram's add-on may stay next to ShortFuse's; its placement below sorts out its own files.
                    foreach (var name in ComponentStore.ForeignNrAddons.Where(n => !o.Lecram || !ComponentStore.LecramFiles.Contains(n, StringComparer.OrdinalIgnoreCase)))
                    {
                        var other = Path.Combine(targetDir, name);
                        if (m.Owns(ctx.Rel(other))) { RemoveOwned(ctx, other); Log.Info($"  removed {ctx.Rel(other)} (replaced by {ComponentStore.RenoDxFile})"); }
                        else if (File.Exists(other)) Displace(ctx, other);
                    }
                    Place(ctx, renoDx.Path, Path.Combine(targetDir, ComponentStore.RenoDxFile));
                    m.RenoDx = true;
                    m.RenoDxTag = renoDx.Build.Tag;
                    m.RenoDxId = renoDx.Build.Id;
                }
                if (lecram is not null)
                {
                    var file = lecram.Build.FileName;
                    // v11 goes inert when v10 is also loaded, so only the chosen build's generation may be in the game.
                    foreach (var name in ComponentStore.LecramFiles.Where(n => !n.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    {
                        var other = Path.Combine(targetDir, name);
                        if (m.Owns(ctx.Rel(other))) { RemoveOwned(ctx, other); Log.Info($"  removed {ctx.Rel(other)} (replaced by {file})"); }
                        if (File.Exists(other)) Displace(ctx, other);
                    }
                    DisplaceForeignNrAddons(ctx, [.. ComponentStore.ForeignNrAddons.Where(n => !ComponentStore.LecramFiles.Contains(n, StringComparer.OrdinalIgnoreCase))]);
                    Place(ctx, lecram.Path, Path.Combine(targetDir, file));
                    m.Lecram = true;
                    m.LecramFile = file;
                    m.LecramTag = lecram.Build.Tag;
                    m.LecramId = lecram.Build.Id;
                }
                if (dfc is not null)
                {
                    DisplaceForeignNrAddons(ctx, [.. ComponentStore.RenoDxFamily, .. ComponentStore.DfcForeignFiles, .. ComponentStore.UpliftFiles, .. ComponentStore.LegacyUpliftFiles]);
                    PlaceDfc(ctx);
                    PlaceDfcRuntimes(ctx, dfcRuntimes ? dlss : null, o.DfcFrameGen);
                    WriteDfcConfig(ctx, o);
                    m.Dfc = true;
                    m.DfcTag = store.DfcTag;
                }
                if (uplift is not null)
                {
                    DisplaceForeignNrAddons(ctx, [ComponentStore.RenoDxFile, .. ComponentStore.ForeignNrAddons.Where(f => !ComponentStore.UpliftFiles.Contains(f))]);
                    foreach (var rel in ComponentStore.UpliftFiles) Place(ctx, Path.Combine(uplift, rel), Path.Combine(targetDir, rel));
                    m.Uplift = true;
                    m.UpliftTag = o.UpliftTag ?? store.Uplift?.Tag;
                }
                if (nrPath is not null) PlaceDlssNr(ctx, nrPath);

                if (dlss is not null)
                {
                    SwapDlss(ctx, dlss, replace: o.Dlss, addMissing, exact: o.DlssTag is not null);
                    if (o.Dlss)
                    {
                        m.Dlss = true;
                        m.DlssTag = store.DlssFor(o.DlssTag)?.Tag;
                        m.DlssPinned = o.DlssTag is not null;
                    }
                }
                if (sl is not null)
                {
                    SwapStreamline(ctx, sl);
                    m.Streamline = true;
                    m.StreamlineTag = store.Streamline?.Tag;
                }
                m.AntiCheatConfirmed |= o.AntiCheatConfirmed;
                Log.Info($"{game.Name}: done ({targetDir})");
            }
            catch (UnauthorizedAccessException) { throw new NeedsAdminException(targetDir); }
            catch (IOException ex) when (IsSharingViolation(ex)) { throw new GameFileInUseException(game.Name, ex); }
            finally
            {
                if (!m.IsEmpty) m.Save(targetDir);
            }
        }, ct);
    }

    private static void InstallOpti(Ctx ctx, string pkg, InstallOptions o, OptiSource source)
    {
        var m = ctx.M;

        // Other proxies that are OptiScaler, plus files the fork no longer uses.
        foreach (var name in AppSettings.ProxyNames.Where(n => !n.Equals(o.Proxy, StringComparison.OrdinalIgnoreCase)))
        {
            var f = Path.Combine(ctx.Target, name);
            if (File.Exists(f) && IsOptiScaler(f)) Displace(ctx, f);
        }
        foreach (var name in LegacyFiles.Where(n => !source.ExtraFiles.Contains(n, StringComparer.OrdinalIgnoreCase)))
        {
            var f = Path.Combine(ctx.Target, name);
            if (File.Exists(f)) Displace(ctx, f);
        }

        Place(ctx, Path.Combine(pkg, "OptiScaler.dll"), Path.Combine(ctx.Target, o.Proxy));
        foreach (var name in source.ExtraFiles)
        {
            var extra = Path.Combine(pkg, name);
            if (File.Exists(extra)) Place(ctx, extra, Path.Combine(ctx.Target, name));
        }

        foreach (var dir in PackageDirs)
        {
            var src = Path.Combine(pkg, dir);
            if (!Directory.Exists(src)) continue;
            var dest = Path.Combine(ctx.Target, dir);
            var owned = m.Dirs.Contains(ctx.Rel(dest), StringComparer.OrdinalIgnoreCase);
            if (owned || !Directory.Exists(dest))
            {
                if (!owned)
                {
                    m.AddDir(ctx.Rel(dest));
                    Persist(ctx);
                }
                MirrorDir(src, dest);
            }
            else
            {
                // Folder predates us (e.g. an earlier manual install): track file by file so uninstall leaves it as it was.
                foreach (var file in PackageFiles(src))
                    Place(ctx, file, Path.Combine(dest, Path.GetRelativePath(src, file)));
            }
        }

        var iniPath = Path.Combine(ctx.Target, "OptiScaler.ini");
        var releaseIni = File.ReadAllText(Path.Combine(pkg, "OptiScaler.ini"));
        string? currentIni = File.Exists(iniPath) ? File.ReadAllText(iniPath) : null;
        if (currentIni is not null && !m.Owns(ctx.Rel(iniPath))) Backup(ctx, iniPath, "file", copy: true);
        var profile = ConfigProfile.WithRequired(o.Overrides, o);
        // Without a ReShade64.dll beside it, LoadReshade from the profile goes back to the release's value (off).
        if (!o.ReShade && !File.Exists(Path.Combine(ctx.Target, ComponentStore.ReShadeFile)))
            profile.RemoveAll(x => x.Id.Equals("Plugins/LoadReshade", StringComparison.OrdinalIgnoreCase));
        if (currentIni is not null && m.Owns(ctx.Rel(iniPath)) && m.OptiIni.Count == 0)
        {
            // Installs from 1.0.0 didn't record what they wrote; values still equal to the profile are ours.
            var cur = IniFile.Parse(currentIni);
            foreach (var ov in profile)
                if (string.Equals(cur.Get(ov.Section, ov.Key), ov.Value, StringComparison.OrdinalIgnoreCase)) m.OptiIni[ov.Id] = ov.Value;
        }
        var merged = ConfigProfile.Merge(releaseIni, currentIni, profile, o.CarryOverIni, m.OptiIni, o.OverwriteIni);
        if (!o.CarryOverIni && currentIni is not null && m.Owns(ctx.Rel(iniPath)) && merged.Text != currentIni) KeepPrevious(ctx, iniPath);
        if (!m.Owns(ctx.Rel(iniPath)))
        {
            m.AddFile(ctx.Rel(iniPath));
            Persist(ctx);
        }
        FileUtil.AtomicWriteText(iniPath, merged.Text);
        m.OptiIni = merged.Applied;
        Log.Info($"  {o.Proxy} ← OptiScaler-NR, OptiScaler.ini merged");
    }

    /// <summary>
    /// ReShade.ini sits next to ReShade64.dll. ReShade's own settings stay; fresh mode clears the installed
    /// add-ons' sections before applying our profile. Otherwise user edits are kept unless overwrite is selected.
    /// <paramref name="addonEffects"/>: Add-on effects go to reshade-shaders\Shaders, which EffectSearchPaths must cover.
    /// </summary>
    private static void WriteReShadeIni(Ctx ctx, InstallOptions o, bool addonEffects, LecramGeneration? lecramGen)
    {
        var path = Path.Combine(ctx.Target, "ReShade.ini");
        var rel = ctx.Rel(path);
        var exists = File.Exists(path);
        var original = exists ? File.ReadAllText(path) : null;
        var profile = o.ReShadeOverrides
            .Where(x => o.RenoDx || !RenoDxIni.IsRenoDx(x))
            .Where(x => o.Lecram || !LecramIni.IsLecram(x))
            .Where(x => o.Uplift || !UpliftConfig.IsUplift(x))
            .Where(x => !DfcConfig.IsDfc(x)).ToList();
        // Without a version marker, NeuralUplift makes v11 treat even a fresh profile as v10 and reset its hook point.
        if (!o.CarryOverIni && o.Lecram && lecramGen == LecramGeneration.V11 &&
            profile.Any(x => LecramIni.IsLecram(x) && x.Key.Trim().Equals("NRHookPoint", StringComparison.OrdinalIgnoreCase)) &&
            profile.Any(x => LecramIni.IsLecram(x) && x.Key.Trim().Equals("NeuralUplift", StringComparison.OrdinalIgnoreCase)))
        {
            profile.RemoveAll(x => LecramIni.IsLecram(x) && x.Key.Trim().Equals("ConfigVersion", StringComparison.OrdinalIgnoreCase));
            profile.Add(new(LecramIni.Section, "ConfigVersion", "11"));
        }
        // Uplift's default is off; written as a profile value so turning it off in the overlay later sticks.
        if (o.Uplift)
        {
            profile.RemoveAll(x => UpliftConfig.IsUplift(x) && x.Key.Equals("Enabled", StringComparison.OrdinalIgnoreCase));
            profile.Add(new(UpliftConfig.Section, "Enabled", "1"));
        }
        // Next to ShortFuse's add-on Lecram stands down unless told to observe; the user's own ForeignNr wins.
        var explicitForeignNr = profile.Any(LecramIni.IsForeignNr);
        var observe = o.RenoDx && o.Lecram && !explicitForeignNr;
        if (observe) profile.Add(new(LecramIni.Section, LecramIni.ForeignNrKey, "observe"));
        if (o.RenoDx)
        {
            var addons = EarlyAddons(IniFile.Parse(original ?? ""));
            addons.RemoveAll(a => ComponentStore.LegacyRenoDxFiles.Contains(a, StringComparer.OrdinalIgnoreCase));
            if (!addons.Contains(ComponentStore.RenoDxFile, StringComparer.OrdinalIgnoreCase)) addons.Add(ComponentStore.RenoDxFile);
            profile.RemoveAll(x => x.Id.Equals("ADDON/LoadFromDllMain", StringComparison.OrdinalIgnoreCase));
            profile.Add(new("ADDON", "LoadFromDllMain", string.Join(',', addons)));
        }

        // Keys an earlier version wrote for the wrong add-on go, unless the user changed them since.
        var cleaned = IniFile.Parse(original ?? "");
        var hadStale = false;
        if (!o.CarryOverIni)
        {
            if (o.RenoDx)
            {
                hadStale |= cleaned.RemoveSection(RenoDxIni.Section);
                hadStale |= cleaned.RemoveSection(RenoDxIni.PresetSection);
            }
            if (o.Lecram) hadStale |= cleaned.RemoveSection(LecramIni.Section);
            if (o.Uplift) hadStale |= cleaned.RemoveSection(UpliftConfig.Section);
        }
        // The section also held keys of a different add-on that older versions of this app wrote; Lecram's own keys stay.
        hadStale |= !o.Lecram && DropWrittenKeys(cleaned, ctx.M.ReShadeIni, LecramIni.Section);
        hadStale |= !o.Uplift && DropWrittenKeys(cleaned, ctx.M.ReShadeIni, UpliftConfig.Section);
        // Uplift saves Enabled=0 itself; until we have written Enabled once, that is its default, not the user's choice.
        if (o.Uplift && !ctx.M.ReShadeIni.ContainsKey($"{UpliftConfig.Section}/Enabled"))
            hadStale |= cleaned.Remove(UpliftConfig.Section, "Enabled");
        // The add-on writes ForeignNr=yield itself on first run; that is its default, not a choice, so it makes way for observe.
        if (observe && string.Equals(cleaned.Get(LecramIni.Section, LecramIni.ForeignNrKey), "yield", StringComparison.OrdinalIgnoreCase))
            hadStale |= cleaned.Remove(LecramIni.Section, LecramIni.ForeignNrKey);
        // observe was only needed next to ShortFuse's add-on.
        if (o.Lecram && !o.RenoDx && !explicitForeignNr)
            hadStale |= DropWrittenKeys(cleaned, ctx.M.ReShadeIni, LecramIni.Section, k => k.Equals(LecramIni.ForeignNrKey, StringComparison.OrdinalIgnoreCase));
        hadStale |= DropWrittenKeys(cleaned, ctx.M.ReShadeIni, RenoDxIni.Section, RenoDxIni.IsPresetKey);
        if (o.CarryOverIni && o.Lecram && lecramGen == LecramGeneration.V11 &&
            profile.Any(x => LecramIni.IsLecram(x) && x.Key.Trim().Equals("NRHookPoint", StringComparison.OrdinalIgnoreCase)))
            hadStale |= LecramIni.MigrateToV11(cleaned);
        var needsEffectPath = addonEffects && DfcConfig.EffectSearchPathsFor(cleaned.Get("GENERAL", "EffectSearchPaths"), ctx.Target) is not null;
        if (profile.Count == 0 && ctx.M.ReShadeIni.Count == 0 && !hadStale && !needsEffectPath) return;

        var current = hadStale ? cleaned.ToString() : original;
        var merged = ConfigProfile.Merge(current ?? "", current, profile, carryOver: true, ctx.M.ReShadeIni,
            overwrite: o.OverwriteIni || !o.CarryOverIni);
        if (o.RenoDx)
        {
            var ini = IniFile.Parse(merged.Text);
            var early = profile.First(x => x.Id == "ADDON/LoadFromDllMain");
            ini.Set(early.Section, early.Key, early.Value);
            merged.Applied[early.Id] = early.Value;
            merged = merged with { Text = ini.ToString() };
        }
        // Not recorded as a profile value: the folder is ReShade's standard one and stays covered when DFC goes.
        if (addonEffects && IniFile.Parse(merged.Text) is var withPaths
            && DfcConfig.EffectSearchPathsFor(withPaths.Get("GENERAL", "EffectSearchPaths"), ctx.Target) is { } paths)
        {
            withPaths.Set("GENERAL", "EffectSearchPaths", paths);
            merged = merged with { Text = withPaths.ToString() };
        }
        if (original == merged.Text) { ctx.M.ReShadeIni = merged.Applied; return; }

        if (exists && !ctx.M.Owns(rel) && ctx.M.BackupOf(rel) is null) Backup(ctx, path, "file", copy: true);
        if (!exists) RecordNew(ctx, rel);
        FileUtil.AtomicWriteText(path, merged.Text);
        ctx.M.ReShadeIni = merged.Applied;
    }

    private static List<string> EarlyAddons(IniFile ini) =>
        (ini.Get("ADDON", "LoadFromDllMain") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Removes keys of <paramref name="section"/> (only those matching <paramref name="keyFilter"/>, if given) that still hold the value we wrote, and forgets all we wrote there.</summary>
    private static bool DropWrittenKeys(IniFile ini, Dictionary<string, string> applied, string section, Func<string, bool>? keyFilter = null)
    {
        var dropped = false;
        foreach (var (id, value) in applied.ToList())
        {
            var slash = id.IndexOf('/');
            if (slash <= 0 || !id[..slash].Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            var key = id[(slash + 1)..];
            if (keyFilter is not null && !keyFilter(key)) continue;
            if (string.Equals(ini.Get(section, key), value, StringComparison.OrdinalIgnoreCase)) dropped |= ini.Remove(section, key);
            applied.Remove(id);
        }
        return dropped;
    }

    /// <summary>
    /// The install no longer has the RenoDX add-on: take it out (restoring what it replaced), our entry out of the
    /// early-load list and the settings we wrote for it out of ReShade.ini.
    /// </summary>
    private static void RemoveRenoDx(Ctx ctx)
    {
        var m = ctx.M;
        foreach (var name in new[] { ComponentStore.RenoDxFile }.Concat(ComponentStore.LegacyRenoDxFiles))
            RemoveOwned(ctx, Path.Combine(ctx.Target, name));
        var path = Path.Combine(ctx.Target, "ReShade.ini");
        if (File.Exists(path))
        {
            var ini = IniFile.Load(path);
            var addons = EarlyAddons(ini);
            var kept = addons.Where(a => !a.Equals(ComponentStore.RenoDxFile, StringComparison.OrdinalIgnoreCase)
                                         && !ComponentStore.LegacyRenoDxFiles.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
            var changed = kept.Count != addons.Count;
            if (changed)
            {
                if (kept.Count == 0) ini.Remove("ADDON", "LoadFromDllMain");
                else ini.Set("ADDON", "LoadFromDllMain", string.Join(',', kept));
                if (kept.Count == 0) m.ReShadeIni.Remove("ADDON/LoadFromDllMain");
                else if (m.ReShadeIni.ContainsKey("ADDON/LoadFromDllMain")) m.ReShadeIni["ADDON/LoadFromDllMain"] = string.Join(',', kept);
            }
            changed |= DropWrittenKeys(ini, m.ReShadeIni, RenoDxIni.Section);
            changed |= DropWrittenKeys(ini, m.ReShadeIni, RenoDxIni.PresetSection);
            if (changed) ini.Save(path);
        }
        m.RenoDx = false;
        m.RenoDxTag = null;
        m.RenoDxId = null;
        Log.Info($"  {ComponentStore.RenoDxFile} removed");
    }

    /// <summary>
    /// The install no longer has Lecram's add-on: take the file we placed out (restoring what it replaced) and the
    /// settings we wrote for it out of ReShade.ini.
    /// </summary>
    private static void RemoveLecram(Ctx ctx)
    {
        var m = ctx.M;
        foreach (var name in ComponentStore.LecramFiles)
            RemoveOwned(ctx, Path.Combine(ctx.Target, name));
        var path = Path.Combine(ctx.Target, "ReShade.ini");
        if (File.Exists(path))
        {
            var ini = IniFile.Load(path);
            if (DropWrittenKeys(ini, m.ReShadeIni, LecramIni.Section)) ini.Save(path);
        }
        m.Lecram = false;
        m.LecramTag = null;
        m.LecramId = null;
        m.LecramFile = null;
        Log.Info("  Lecram's DLSS5 add-on removed");
    }

    /// <summary>The install no longer has MFG Unlock: take the add-on out (restoring what it replaced).</summary>
    private static void RemoveMfg(Ctx ctx)
    {
        RemoveOwned(ctx, Path.Combine(ctx.Target, ComponentStore.MfgFile));
        ctx.M.Mfg = false;
        ctx.M.MfgTag = null;
        Log.Info($"  {ComponentStore.MfgFile} removed");
    }

    /// <summary>OptiScaler mode without ReShade any more: take out the ReShade64.dll we placed (restoring what it replaced).</summary>
    private static void RemoveReShade(Ctx ctx)
    {
        RemoveOwned(ctx, Path.Combine(ctx.Target, ComponentStore.ReShadeFile));
        ctx.M.ReShade = false;
        ctx.M.ReShadeSha = null;
        Log.Info($"  {ComponentStore.ReShadeFile} removed");
    }

    private static void RemoveUplift(Ctx ctx)
    {
        foreach (var rel in ComponentStore.UpliftFiles) RemoveOwned(ctx, Path.Combine(ctx.Target, rel));
        var path = Path.Combine(ctx.Target, "ReShade.ini");
        if (File.Exists(path))
        {
            var ini = IniFile.Load(path);
            if (DropWrittenKeys(ini, ctx.M.ReShadeIni, UpliftConfig.Section)) ini.Save(path);
        }
        ctx.M.Uplift = false;
        ctx.M.UpliftTag = null;
    }

    /// <summary>
    /// deep-fried-chicken.cfg: fresh mode starts from the imported default, otherwise the game's cfg is kept.
    /// The app's DFC options are merged in, preserving overlay edits unless overwrite is selected.
    /// A cfg that was already there is backed up once, before the first change.
    /// </summary>
    private void WriteDfcConfig(Ctx ctx, InstallOptions o)
    {
        var m = ctx.M;
        var path = Path.Combine(ctx.Target, ComponentStore.DfcConfigFile);
        var rel = ctx.Rel(path);
        var exists = File.Exists(path);
        var profile = o.ReShadeOverrides.Where(DfcConfig.IsDfc).ToList();
        var defaults = File.Exists(store.DfcConfigPath) ? File.ReadAllText(store.DfcConfigPath) : null;
        if (!exists && profile.Count == 0 && m.DfcCfg.Count == 0)
        {
            if (defaults is not null) Place(ctx, store.DfcConfigPath, path);
            return;
        }
        if (o.CarryOverIni && exists && profile.Count == 0 && m.DfcCfg.Count == 0) return;

        var original = exists ? File.ReadAllText(path) : null;
        var current = o.CarryOverIni ? original : null;
        var merged = DfcConfig.Merge(current ?? defaults ?? "", current, defaults, profile, m.DfcCfg, o.OverwriteIni || !o.CarryOverIni);
        m.DfcCfg = merged.Applied;
        if (original == merged.Text) return;
        if (exists && !m.Owns(rel) && m.BackupOf(rel) is null) Backup(ctx, path, "file", copy: true);
        else if (exists && !o.CarryOverIni) KeepPrevious(ctx, path);
        if (!exists) RecordNew(ctx, rel);
        FileUtil.AtomicWriteText(path, merged.Text);
    }

    /// <summary>A file we are about to create is recorded (and saved) first, so a run stopped part-way never leaves it unrecorded.</summary>
    private static void RecordNew(Ctx ctx, string rel)
    {
        ctx.M.AddFile(rel);
        Persist(ctx);
    }

    /// <summary>
    /// Fresh mode is about to rebuild a config whose original is already saved (or that we created), which would drop the
    /// user's edits since: the version it replaces is kept as .dlssupdater\previous\&lt;name&gt;, one copy per file.
    /// </summary>
    private static void KeepPrevious(Ctx ctx, string path)
    {
        var dest = Path.Combine(InstallManifest.DirFor(ctx.Target), PreviousDir, Path.GetFileName(path));
        if (FileUtil.LinkBelow(ctx.Target, dest) is { } link) throw new InvalidOperationException($"Couldn't keep a copy of the previous {Path.GetFileName(path)}: {link} is a link. Remove the link in {InstallManifest.DirName} and try again.");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        FileUtil.ClearReadOnly(dest);
        FileUtil.AtomicCopy(path, dest);
        FileUtil.ClearReadOnly(dest);
        Log.Info($"  previous {Path.GetFileName(path)} kept in {Path.Combine(InstallManifest.DirName, PreviousDir)}");
    }

    private const string PreviousDir = "previous";

    /// <summary>
    /// Copies the imported 64-bit set into the game with the package's layout (add-on, bridge, present-support dll, the
    /// neural folder file by file and DFC's ReShade effects), then takes out files an earlier set of ours had and this one lacks.
    /// </summary>
    private void PlaceDfc(Ctx ctx)
    {
        var files = store.DfcInstallFiles();
        foreach (var rel in files)
            Place(ctx, Path.Combine(ComponentStore.DfcDir, rel), Path.Combine(ctx.Target, rel));
        foreach (var full in OwnedDfcFiles(ctx).Where(f => !files.Contains(Path.GetRelativePath(ctx.Target, f), StringComparer.OrdinalIgnoreCase)))
        {
            RemoveOwned(ctx, full);
            Log.Info($"  removed {ctx.Rel(full)} (not in the imported Deep Fried Chicken set)");
        }
    }

    /// <summary>Files of the DFC set (not the cfg) this install placed, as full paths.</summary>
    private static List<string> OwnedDfcFiles(Ctx ctx) =>
        ctx.M.Files.Select(ctx.Full)
            .Where(f => FileUtil.IsUnder(f, ctx.Target) && ComponentStore.IsDfcPackagePath(Path.GetRelativePath(ctx.Target, f)))
            .ToList();

    /// <summary>Takes out Deep Fried Chicken's files we placed (restoring what they replaced).</summary>
    private static void RemoveDfc(Ctx ctx)
    {
        foreach (var full in OwnedDfcFiles(ctx).Append(Path.Combine(ctx.Target, ComponentStore.DfcConfigFile)))
            RemoveOwned(ctx, full);
        RemoveDfcRuntimes(ctx, keep: []);
        // A cfg that was already there and changed by us is not owned, but its original is saved.
        if (ctx.M.BackupOf(ctx.Rel(Path.Combine(ctx.Target, ComponentStore.DfcConfigFile))) is { } b)
        {
            Restore(ctx, b);
            ctx.M.Backups.Remove(b);
        }
        ctx.M.DfcCfg.Clear();
        ctx.M.Dfc = false;
        ctx.M.DfcTag = null;
        Log.Info($"  {ComponentStore.DfcFile} removed");
    }

    /// <summary>
    /// DFC in a game without its own DLSS: INSTALL.txt (4.0.6) asks for nvngx_dlss.dll beside the game exe, and for
    /// nvngx_dlssg.dll there too when its DLSS-G frame generation is used; DFC ships neither. Both come from NVIDIA's DLSS
    /// release (<paramref name="dlss"/>) and are kept current with it. Null <paramref name="dlss"/> (a game with native
    /// DLSS) wants none. Runtimes placed earlier that are no longer wanted go; a copy we don't own is never touched.
    /// </summary>
    private static void PlaceDfcRuntimes(Ctx ctx, IReadOnlyDictionary<string, string>? dlss, bool frameGen)
    {
        string[] wanted = dlss is null ? [] : frameGen ? DfcRuntimeFiles : DfcRuntimeFiles[..1];
        var placed = new List<string>();
        foreach (var name in wanted)
        {
            var dest = Path.Combine(ctx.Target, name);
            var rel = ctx.Rel(dest);
            if (!dlss!.TryGetValue(name, out var src))
            {
                Log.Warn($"  {name}: not in the DLSS release, so Deep Fried Chicken can't get it here");
                continue;
            }
            if (File.Exists(dest) && !ctx.M.Owns(rel))
            {
                Log.Info($"  {rel}: already there and not ours, left as is for Deep Fried Chicken");
                continue;
            }
            if (!File.Exists(dest) || !FileUtil.SameFile(src, dest))
            {
                var had = File.Exists(dest);
                Place(ctx, src, dest);
                Log.Info($"  {(had ? "updated" : "added")} {rel} for Deep Fried Chicken ({FileUtil.Format(FileUtil.ReadVersion(src))})");
            }
            placed.Add(rel);
        }
        RemoveDfcRuntimes(ctx, keep: placed);
        foreach (var rel in placed)
            if (!ctx.M.DfcRuntimes.Contains(rel, StringComparer.OrdinalIgnoreCase)) ctx.M.DfcRuntimes.Add(rel);
    }

    /// <summary>nvngx_dlss.dll first: it is always wanted; nvngx_dlssg.dll only for frame generation.</summary>
    private static readonly string[] DfcRuntimeFiles = ["nvngx_dlss.dll", "nvngx_dlssg.dll"];

    /// <summary>Takes out the runtimes placed for DFC except <paramref name="keep"/>, restoring what they replaced.</summary>
    private static void RemoveDfcRuntimes(Ctx ctx, IReadOnlyCollection<string> keep)
    {
        foreach (var rel in ctx.M.DfcRuntimes.Where(r => !keep.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            if (ctx.M.Owns(rel)) Log.Info($"  removed {rel} (placed for Deep Fried Chicken)");
            RemoveOwned(ctx, ctx.Full(rel));
            ctx.M.DfcRuntimes.Remove(rel);
        }
    }

    /// <summary>The refusal for an imported DFC older than 4.x in a game without its own DLSS.</summary>
    internal static string DfcNeedsNativeDlss(string? version) =>
        $"Deep Fried Chicken {version} needs a game with native DLSS. Import Deep Fried Chicken {ComponentStore.DfcNonDlssMajor}.x, which also supports games without their own DLSS.";

    /// <summary>
    /// Whether the game ships DLSS Super Resolution itself (nvngx_dlss.dll or Streamline's sl.dlss.dll), not counting the
    /// copies an install of ours added beside the exe (for DFC, or OptiScaler's "add missing DLSS").
    /// </summary>
    public static bool HasNativeDlss(GameInfo game, InstallManifest? m, string targetDir) =>
        game.Dlss.Concat(game.Streamline)
            .Where(d => GameInfo.FeaturesOf([d], []).HasFlag(GameFeatures.SR))
            .Any(d => m is null || !OwnedBy(m, d.Path, targetDir, game.Root));

    private static bool OwnedBy(InstallManifest m, string path, string targetDir, string root)
    {
        if (string.IsNullOrEmpty(path)) return false;
        // A DLSS update replaces the game's own file: ours now, but the backed-up original makes the game native.
        try
        {
            var rel = InstallManifest.Entry(FileUtil.Normalize(path), FileUtil.Normalize(targetDir), FileUtil.Normalize(root));
            return m.Owns(rel) && m.BackupOf(rel) is null;
        }
        catch (UnsafeManifestException) { return false; }
    }

    /// <summary>Another NR injector next to OptiScaler's DLSSNR would run the model twice: move the ones we don't own away.</summary>
    private static void DisplaceForeignNrAddons(Ctx ctx, params string[] names)
    {
        foreach (var name in names)
        {
            var f = Path.Combine(ctx.Target, name);
            if (File.Exists(f) && !ctx.M.Owns(ctx.Rel(f))) Displace(ctx, f);
        }
    }

    /// <summary>Checks the game's Streamline / DLSS-G against MFG Unlock, with the DLSS update this run applies.</summary>
    internal static MfgPlan PlanMfg(GameInfo game, InstallOptions o, IReadOnlyDictionary<string, string>? dlss, Version? sdk)
    {
        var fg = game.FgVersion;
        if (o.Dlss && dlss is not null && dlss.TryGetValue("nvngx_dlssg.dll", out var src) && FileUtil.ReadVersion(src) is { } newFg)
            fg = o.DlssTag is not null || fg is null || newFg > fg ? newFg : fg;
        var dynamic = o.ReShadeOverrides.Any(x => x.Section.Equals("RenoDX.MFGUnlock", StringComparison.OrdinalIgnoreCase)
                                                && x.Key.Equals("DynamicMFG", StringComparison.OrdinalIgnoreCase) && x.Value == "1");
        return MfgCompat.Plan(game.Streamline, fg, sdk, o.Streamline, dynamic, o.RenoDx || o.Lecram);
    }

    /// <summary>
    /// OptiScaler-NR v0.9+ can unlock MFG itself ([DLSSG] AdaMfgUnlock / AmpereMfgUnlock) and says never to combine that
    /// with an external unlocker, which the MFG Unlock add-on is. Checked before anything is fetched or written. The add-on
    /// already in the game counts too while this run leaves it there (<see cref="InstallOptions.KeepMfg"/>).
    /// </summary>
    private static void EnsureNoDoubleMfgUnlock(string targetDir, InstallOptions o)
    {
        var addonStays = o.KeepMfg && o.Opti && InstallManifest.Read(targetDir)?.Mfg == true;
        if (!(o.Mfg || addonStays) || o.ReShadeOnly) return;
        var iniPath = Path.Combine(targetDir, "OptiScaler.ini");
        // An MFG-only update still runs next to the OptiScaler already in the game.
        if (!o.Opti) { if (!File.Exists(iniPath)) return; o = o with { Overrides = [], CarryOverIni = true }; }
        var current = o.CarryOverIni && File.Exists(iniPath) ? File.ReadAllText(iniPath) : null;
        var lastApplied = InstallManifest.Read(targetDir)?.OptiIni;
        var effective = IniFile.Parse(ConfigProfile.Merge(current ?? "", current, o.Overrides, o.CarryOverIni, lastApplied, o.OverwriteIni).Text);
        var on = new[] { "AdaMfgUnlock", "AmpereMfgUnlock" }
            .Where(k => effective.Get("DLSSG", k) is { } v && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1")).ToList();
        if (on.Count > 0)
            throw new InvalidOperationException(
                $"OptiScaler's built-in MFG unlock ([DLSSG] {string.Join(", ", on)}) is on, and it must not run together with the MFG Unlock add-on. " +
                "Turn one of them off.");
    }

    /// <summary>Copies the package folder in place, skipping unchanged files and docs.</summary>
    private static IEnumerable<string> PackageFiles(string src) =>
        Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

    private static void MirrorDir(string src, string dest)
    {
        foreach (var file in PackageFiles(src))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(src, file));
            if (FileUtil.SameFile(file, target)) continue;
            FileUtil.AtomicCopy(file, target);
        }
    }

    /// <summary>
    /// Replaces every sl.*.dll the game ships with the same file from one Streamline release, so the set
    /// never mixes versions. Plugins the SDK doesn't have (rare, game-specific) are left alone and logged.
    /// </summary>
    private static void SwapStreamline(Ctx ctx, string slDir)
    {
        var m = ctx.M;
        var scan = new GameInfo { Root = ctx.Root };
        GameInspector.Inspect(scan);
        if (ClaimedByOtherInstalls(ctx, scan, "Streamline files") is not { } claimed) return;
        foreach (var dll in scan.Streamline)
        {
            var rel = ctx.Rel(dll.Path);
            var src = Path.Combine(slDir, dll.Name);
            if (!File.Exists(src))
            {
                Log.Info($"  {rel}: not in the Streamline SDK, left as is");
                continue;
            }
            if (FileUtil.SameFile(src, dll.Path)) continue;
            if (LeftToOtherInstall(ctx, claimed, dll.Path)) continue;
            var entry = m.BackupOf(rel);
            var ours = entry?.InstalledSha is { } s && s == HashCache.Get(dll.Path);
            Replace(ctx, src, dll.Path, ours ? null : "streamline", installedSha: HashCache.Get(src));
            Log.Info($"  {rel}: {FileUtil.Format(dll.Version)} → {FileUtil.Format(FileUtil.ReadVersion(src))}");
        }
    }

    /// <summary>
    /// <paramref name="replace"/>: update every DLSS dll the game has. <paramref name="addMissing"/>: give OptiScaler an
    /// nvngx_dlss.dll when the game ships none (or only the one we added earlier, which is then kept current).
    /// </summary>
    private static void SwapDlss(Ctx ctx, IReadOnlyDictionary<string, string> latest, bool replace, bool addMissing, bool exact = false)
    {
        var m = ctx.M;
        var scan = new GameInfo { Root = ctx.Root };
        GameInspector.Inspect(scan);
        var claimed = replace ? ClaimedByOtherInstalls(ctx, scan, "DLSS files") : null;

        foreach (var dll in claimed is not null ? scan.Dlss : [])
        {
            if (FileUtil.IsUnder(dll.Path, InstallManifest.DirFor(ctx.Target))) continue;
            if (!latest.TryGetValue(dll.Name, out var src)) continue;
            var newVer = FileUtil.ReadVersion(src);
            var rel = ctx.Rel(dll.Path);
            // Latest: only ever upgrade. Pinned: move to exactly that version, up or down.
            if (dll.Version is not null && newVer is not null && (exact ? dll.Version == newVer : dll.Version >= newVer)) continue;
            if (LeftToOtherInstall(ctx, claimed!, dll.Path)) continue;

            var srcSha = HashCache.Get(src);
            var entry = m.BackupOf(rel);
            var ours = m.Owns(rel) || (entry?.InstalledSha is { } s && s == HashCache.Get(dll.Path));
            Replace(ctx, src, dll.Path, ours ? null : "dlss", installedSha: srcSha);
            Log.Info($"  {rel}: {FileUtil.Format(dll.Version)} → {FileUtil.Format(newVer)}");
        }

        var sr = scan.Dlss.Where(d => d.Name.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase)).ToList();
        if (addMissing && latest.TryGetValue("nvngx_dlss.dll", out var srSrc) && sr.All(d => m.Owns(ctx.Rel(d.Path))))
        {
            var dest = Path.Combine(ctx.Target, "nvngx_dlss.dll");
            var had = File.Exists(dest);
            if (had && FileUtil.SameFile(srSrc, dest)) return;
            Place(ctx, srSrc, dest);
            Log.Info($"  {(had ? "updated" : "added")} {ctx.Rel(dest)}");
        }
    }

    /// <summary>
    /// Game files (by full path) another install folder of this game has placed or backed up, with that folder. Swapping
    /// one from here would save that install's copy as the original, and uninstalling both would then lose the real one.
    /// The swap scans the whole game (DLSS often sits in an engine folder, not beside the exe), so such files are left to
    /// the install that holds them. Null when another install's record can't be read or is unsafe: then nobody can tell
    /// which files it holds, and the swap is skipped (logged with <paramref name="what"/>).
    /// </summary>
    private static Dictionary<string, string>? ClaimedByOtherInstalls(Ctx ctx, GameInfo scan, string what)
    {
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in scan.Installs.Select(FileUtil.Normalize).Where(d => !d.Equals(ctx.Target, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var other = InstallManifest.Read(dir);
                if (other is null) continue;
                other.Contain(dir, ctx.Root);
                foreach (var entry in other.Files.Concat(other.Backups.Select(b => b.Original)))
                    claimed.TryAdd(InstallManifest.Resolve(entry, dir, ctx.Root), dir);
            }
            catch (Exception ex) when (ex is InvalidDataException or UnsafeManifestException)
            {
                Log.Warn($"  {what} left as they are: the install record in {dir} can't be used ({ex.Message}), so it is unclear " +
                         "which game files that install holds. Repair or uninstall that install, then update again.");
                return null;
            }
        }
        return claimed;
    }

    /// <summary>True when another install holds <paramref name="full"/> and this one has no claim on it.</summary>
    private static bool LeftToOtherInstall(Ctx ctx, Dictionary<string, string> claimed, string full)
    {
        var rel = ctx.Rel(full);
        if (ctx.M.Owns(rel) || ctx.M.BackupOf(rel) is not null || !claimed.TryGetValue(FileUtil.Normalize(full), out var dir)) return false;
        Log.Info($"  {rel}: managed by the install in {dir}, left to it");
        return true;
    }

    /// <summary>Puts <paramref name="src"/> at <paramref name="dest"/>, backing up anything we don't own.</summary>
    private static void Place(Ctx ctx, string src, string dest)
    {
        var rel = ctx.Rel(dest);
        if (File.Exists(dest) && ctx.M.Owns(rel) && FileUtil.SameFile(src, dest)) return;
        Replace(ctx, src, dest, ctx.M.Owns(rel) ? null : "file", own: true);
    }

    /// <summary>
    /// Puts <paramref name="src"/> at <paramref name="dest"/> so the game never goes without the file: the copy is staged
    /// beside it first, the original is saved to the backup (<paramref name="backupKind"/>; null = ours, not saved) and the
    /// copy then takes its place in one rename. A failure leaves <paramref name="dest"/> and the record as they were.
    /// The record of the swap (<paramref name="own"/>: the file is ours from now on; <paramref name="installedSha"/>: the
    /// hash of the file we put in place) is saved before the rename. A run killed part-way then leaves no original that the
    /// record doesn't know, and no newly placed file the record would back up as an original next time; only a file that
    /// was already ours under a reused entry can, in the moment between that save and the rename, look changed by the game.
    /// </summary>
    private static void Replace(Ctx ctx, string src, string dest, string? backupKind, bool own = false, string? installedSha = null)
    {
        var tmp = FileUtil.StageCopy(src, dest);
        var m = ctx.M;
        var rel = ctx.Rel(dest);
        var backedUp = backupKind is not null && File.Exists(dest);
        var entry = m.BackupOf(rel);
        var oldSha = entry?.InstalledSha;
        var wasOwned = m.Owns(rel);
        try
        {
            if (backedUp) Backup(ctx, dest, backupKind!, copy: true);
            var changed = backedUp;
            if (own && !wasOwned) { m.AddFile(rel); changed = true; }
            if (installedSha is not null && m.BackupOf(rel) is { } b && b.InstalledSha != installedSha) { b.InstalledSha = installedSha; changed = true; }
            if (changed) Persist(ctx);
            FileUtil.CommitStaged(tmp, dest);
        }
        catch
        {
            FileUtil.TryDeleteFile(tmp);
            // The original never left. A backup entry made for this swap goes again; a reused one now holds that same original.
            if (entry is null && backedUp && m.BackupOf(rel) is { } made)
            {
                FileUtil.TryDeleteFile(Path.Combine(ctx.BackupDir, made.Backup));
                m.Backups.Remove(made);
            }
            // A reused entry keeps its hash: if the copy into the backup failed, the backup still holds the older original.
            else if (entry is not null) entry.InstalledSha = oldSha;
            if (own && !wasOwned) m.Files.RemoveAll(f => f.Equals(rel, StringComparison.OrdinalIgnoreCase));
            TryPersist(ctx);
            throw;
        }
    }

    /// <summary>Saves the record now, so what is on disk and what it says never drift apart if the run stops here.</summary>
    private static void Persist(Ctx ctx)
    {
        if (ctx.M.IsEmpty) FileUtil.DeleteFile(InstallManifest.PathFor(ctx.Target));
        else ctx.M.Save(ctx.Target);
    }

    private static void TryPersist(Ctx ctx)
    {
        try { Persist(ctx); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Install record in {ctx.Target} not saved: {ex.Message}"); }
    }

    /// <summary>
    /// Switching to ReShade-only: takes out everything an earlier OptiScaler install of ours put here (proxy, package
    /// folders, OptiScaler.ini, ReShade64.dll, DLSSNR) and puts back what those files replaced.
    /// </summary>
    private static void RemoveOpti(Ctx ctx, string newProxy, bool keepNr)
    {
        var m = ctx.M;
        if (m.Mode == InstallMode.OptiScaler && m.Opti && m.Proxy is { } p && !p.Equals(newProxy, StringComparison.OrdinalIgnoreCase))
            RemoveOwned(ctx, Path.Combine(ctx.Target, p));
        foreach (var dir in PackageDirs)
        {
            var rel = ctx.Rel(Path.Combine(ctx.Target, dir));
            if (m.Dirs.Remove(m.Dirs.FirstOrDefault(d => d.Equals(rel, StringComparison.OrdinalIgnoreCase)) ?? ""))
            {
                if (Directory.Exists(ctx.Full(rel))) Directory.Delete(ctx.Full(rel), true);
                continue;
            }
            foreach (var f in m.Files.Where(f => f.StartsWith(rel + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList())
                RemoveOwned(ctx, ctx.Full(f));
        }
        foreach (var name in new[] { "OptiScaler.ini", ComponentStore.ReShadeFile, ComponentStore.DlssNrFile })
            if (!keepNr || name != ComponentStore.DlssNrFile) RemoveOwned(ctx, Path.Combine(ctx.Target, name));
        foreach (var name in OptiSources.All.SelectMany(s => s.ExtraFiles).Distinct(StringComparer.OrdinalIgnoreCase))
            RemoveOwned(ctx, Path.Combine(ctx.Target, name));
        if (m.Opti || m.DlssNr) Log.Info("  OptiScaler-NR removed (ReShade + add-ons mode)");
        m.Opti = false;
        m.OptiTag = null;
        m.OptiIni.Clear();
        if (!keepNr) { m.DlssNr = false; m.DlssNrSha = null; m.DlssNrId = null; }
    }

    /// <summary>Switching back from ReShade-only: the ReShade proxy goes unless OptiScaler takes over the same name.</summary>
    private static void RemoveReShadeProxy(Ctx ctx, string? newProxy)
    {
        if (ctx.M.Proxy is { } p && !p.Equals(newProxy, StringComparison.OrdinalIgnoreCase))
            RemoveOwned(ctx, Path.Combine(ctx.Target, p));
    }

    /// <summary>Deletes a file we placed and restores whatever it replaced. Files we don't own are left alone.</summary>
    private static void RemoveOwned(Ctx ctx, string full)
    {
        var rel = ctx.Rel(full);
        if (!ctx.M.Owns(rel)) return;
        TryDelete(full);
        ctx.M.Files.RemoveAll(f => f.Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (ctx.M.BackupOf(rel) is { } b)
        {
            Restore(ctx, b);
            ctx.M.Backups.Remove(b);
        }
        DeleteEmptyDirs(ctx, Path.GetDirectoryName(full)!);
    }

    /// <summary>Deletes <paramref name="dir"/> and its parents while they are empty: up to the install folder when inside it, else just that one folder.</summary>
    private static void DeleteEmptyDirs(Ctx ctx, string dir)
    {
        var target = FileUtil.Normalize(ctx.Target);
        if (!FileUtil.IsUnder(dir, target))
        {
            FileUtil.TryDeleteEmptyDir(dir);
            return;
        }
        for (var d = FileUtil.Normalize(dir); d.Length > target.Length && FileUtil.IsUnder(d, target); d = Path.GetDirectoryName(d)!)
        {
            FileUtil.TryDeleteEmptyDir(d);
            if (Directory.Exists(d)) return;
        }
    }

    /// <summary>Removes a conflicting file: taken out if we placed it, and whatever is then there moved to the backup.</summary>
    private static void Displace(Ctx ctx, string full)
    {
        var rel = ctx.Rel(full);
        // Ours goes the usual way, which puts back what it replaced; that file must not stay here either.
        RemoveOwned(ctx, full);
        if (File.Exists(full)) Backup(ctx, full, "file");
        Log.Info($"  removed {rel}");
    }

    private static void Backup(Ctx ctx, string full, string kind, bool copy = false)
    {
        var rel = ctx.Rel(full);
        var entry = ctx.M.BackupOf(rel);
        var backupRel = entry?.Backup ?? FreeBackupName(ctx, BackupName(rel));
        var dest = Path.Combine(ctx.BackupDir, backupRel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        FileUtil.ClearReadOnly(dest);
        if (copy) File.Copy(full, dest, true);
        else File.Move(full, dest, true);
        if (entry is null) ctx.M.Backups.Add(new BackupEntry { Original = rel, Backup = backupRel, Kind = kind });
        else entry.InstalledSha = null;
        Persist(ctx);
    }

    /// <summary>The entry as a path inside the backup folder: install-folder entries go under "_install".</summary>
    private static string BackupName(string rel) =>
        rel.StartsWith(InstallManifest.InstallTag, StringComparison.Ordinal)
            ? "_install" + rel[InstallManifest.InstallTag.Length..]
            : rel.Replace("..", "_up");

    /// <summary>A backup name no entry uses and no file holds, so a new backup never overwrites a saved original.</summary>
    private static string FreeBackupName(Ctx ctx, string name)
    {
        var candidate = name;
        for (var i = 1; File.Exists(Path.Combine(ctx.BackupDir, candidate))
                        || ctx.M.Backups.Any(b => b.Backup.Equals(candidate, StringComparison.OrdinalIgnoreCase)); i++)
            candidate = $"{name}.{i}";
        return candidate;
    }

    private void PlaceDlssNr(Ctx ctx, string source)
    {
        var target = ctx.M.Uplift ? UpliftConfig.NrPath(ctx.Target) : Path.Combine(ctx.Target, ComponentStore.DlssNrFile);
        var old = Path.Combine(ctx.Target, ctx.M.DlssNrFile ?? ComponentStore.DlssNrFile);
        var oldVersion = File.Exists(old) ? store.RetainDlssNr(old) : null;
        if (!old.Equals(target, StringComparison.OrdinalIgnoreCase)) RemoveOwned(ctx, old);
        // Uplift refuses a runtime mapped from beside the game exe, including a restored original.
        if (ctx.M.Uplift && File.Exists(Path.Combine(ctx.Target, ComponentStore.DlssNrFile)))
            Displace(ctx, Path.Combine(ctx.Target, ComponentStore.DlssNrFile));
        var selected = store.RetainDlssNr(source);
        var previous = File.Exists(target) ? store.RetainDlssNr(target) : oldVersion;
        Place(ctx, source, target);
        ctx.M.DlssNrFile = Path.GetRelativePath(ctx.Target, target);
        if (previous is not null && previous.Id != selected.Id) ctx.M.PreviousDlssNrId = previous.Id;
        ctx.M.DlssNr = true;
        ctx.M.DlssNrId = selected.Id;
        ctx.M.DlssNrSha = selected.Sha256;
    }

    public Task SwitchDlssNrAsync(GameInfo game, string targetDir, string? id, CancellationToken ct) => Demo.Active ? Demo.SwitchNrAsync(targetDir, id, ct) : Task.Run(() =>
    {
        targetDir = FileUtil.Normalize(targetDir);
        EnsureNotRunning(game.Root);
        EnsureWritable(targetDir);
        var m = InstallManifest.Read(targetDir) ?? throw new InvalidOperationException("Install NR in this game first.");
        m.Contain(targetDir, game.Root);
        if (!m.DlssNr) throw new InvalidOperationException("Install NR in this game first.");
        var ctx = new Ctx(FileUtil.Normalize(game.Root), targetDir, m);
        var source = store.VerifiedDlssNrPath(id ?? store.SelectedDlssNr());
        var target = m.Uplift ? UpliftConfig.NrPath(targetDir) : Path.Combine(targetDir, ComponentStore.DlssNrFile);
        var previous = File.Exists(target) ? store.RetainDlssNr(target) : null;
        try
        {
            PlaceDlssNr(ctx, source);
            m.Save(targetDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed record write must not leave the game on an unrecorded version.
            if (previous is not null) FileUtil.AtomicCopy(store.VerifiedDlssNrPath(previous.Id), target);
            else FileUtil.DeleteFile(target);
            if (ex is IOException io && IsSharingViolation(io)) throw new GameFileInUseException(game.Name, io);
            throw;
        }
    }, ct);

    // ---------- uninstall / restore ----------

    public Task UninstallAsync(GameInfo game, string targetDir, CancellationToken ct) => Demo.Active ? Demo.UninstallAsync(game, targetDir, ct) : Task.Run(() =>
    {
        targetDir = FileUtil.Normalize(targetDir);
        EnsureNotRunning(game.Root);
        var m = InstallManifest.Read(targetDir) ?? throw new InvalidOperationException("No DLSS Updater install found here.");
        m.Contain(targetDir, game.Root);
        var ctx = new Ctx(FileUtil.Normalize(game.Root), targetDir, m);
        try
        {
            // Every step is recorded as soon as it is done, so a retry after a failure part-way (a locked file)
            // carries on where it stopped and never deletes an original that was already put back.
            foreach (var rel in m.Files.ToList())
            {
                var full = ctx.Full(rel);
                TryDelete(full);
                // Only below the install folder: folders a file of ours needed (DFC's neural folder, reshade-shaders) go when empty.
                if (FileUtil.IsUnder(full, targetDir)) DeleteEmptyDirs(ctx, Path.GetDirectoryName(full)!);
                m.Files.Remove(rel);
                m.Save(targetDir);
            }
            foreach (var rel in m.Dirs.ToList())
            {
                var d = ctx.Full(rel);
                FileUtil.DeleteDirectory(d);
                m.Dirs.Remove(rel);
                m.Save(targetDir);
            }
            foreach (var b in m.Backups.ToList())
            {
                Restore(ctx, b);
                m.Backups.Remove(b);
                m.Save(targetDir);
            }
            foreach (var log in LogFiles) TryDelete(Path.Combine(targetDir, log));
            FileUtil.DeleteDirectory(InstallManifest.DirFor(targetDir));
            Log.Info($"{game.Name}: uninstalled, originals restored");
        }
        catch (UnauthorizedAccessException) { throw new NeedsAdminException(targetDir); }
        catch (IOException ex) when (IsSharingViolation(ex)) { throw new GameFileInUseException(game.Name, ex); }
    }, ct);

    public Task RestoreDlssAsync(GameInfo game, string targetDir, CancellationToken ct) => Demo.Active ? Demo.RestoreDlssAsync(game, targetDir, ct) : Task.Run(() =>
    {
        targetDir = FileUtil.Normalize(targetDir);
        EnsureNotRunning(game.Root);
        var m = InstallManifest.Read(targetDir) ?? throw new InvalidOperationException("No DLSS Updater install found here.");
        m.Contain(targetDir, game.Root);
        var ctx = new Ctx(FileUtil.Normalize(game.Root), targetDir, m);
        try
        {
            foreach (var b in m.Backups.Where(b => b.Kind is "dlss" or "streamline").ToList())
            {
                Restore(ctx, b);
                m.Backups.Remove(b);
            }
            // The runtimes placed for DFC are part of that install, not a DLSS update: they leave with DFC.
            foreach (var rel in m.Files.Where(f => ComponentStore.DlssFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                                                   && !m.DfcRuntimes.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                TryDelete(ctx.Full(rel));
                m.Files.Remove(rel);
            }
            m.Dlss = false;
            m.DlssTag = null;
            m.DlssPinned = false;
            m.Streamline = false;
            m.StreamlineTag = null;
            if (m.IsEmpty) FileUtil.DeleteDirectory(InstallManifest.DirFor(targetDir));
            else m.Save(targetDir);
            Log.Info($"{game.Name}: original DLSS and Streamline files restored");
        }
        catch (UnauthorizedAccessException) { throw new NeedsAdminException(targetDir); }
        catch (IOException ex) when (IsSharingViolation(ex)) { throw new GameFileInUseException(game.Name, ex); }
    }, ct);

    private static void Restore(Ctx ctx, BackupEntry b)
    {
        var src = Path.Combine(ctx.BackupDir, b.Backup);
        if (!File.Exists(src)) return;
        var dest = ctx.Full(b.Original);
        // A game patch replaced the file we put in: the game's newer file stays and the stale original goes.
        if (b.InstalledSha is { } ours && File.Exists(dest) && !string.Equals(HashCache.Get(dest), ours, StringComparison.OrdinalIgnoreCase))
        {
            FileUtil.DeleteFile(src);
            Log.Info($"  {b.Original}: changed by the game since, kept");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        FileUtil.ClearReadOnly(dest);
        File.Move(src, dest, true);
    }

    private static void TryDelete(string path) => FileUtil.DeleteFile(path);

    // ---------- checks ----------

    public static bool IsOptiScaler(string path) =>
        string.Equals(FileUtil.OriginalFilename(path), "OptiScaler.dll", StringComparison.OrdinalIgnoreCase);

    public static bool IsReShade(string path) =>
        FileUtil.OriginalFilename(path) is { } n && n.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase);

    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    private static void EnsureWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(InstallManifest.DirFor(dir));
            var probe = Path.Combine(InstallManifest.DirFor(dir), ".probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (UnauthorizedAccessException) { throw new NeedsAdminException(dir); }
    }

    public static void EnsureNotRunning(string root)
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id <= 4) continue;
                var path = ProcessPath(p.Id);
                if (path is not null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && FileUtil.IsUnder(path, root))
                    throw new GameRunningException(path);
            }
        }
    }

    private static string? ProcessPath(int pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
