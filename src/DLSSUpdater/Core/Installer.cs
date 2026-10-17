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
    public bool Dlss { get; init; }
    /// <summary>Put nvngx_dlss.dll next to OptiScaler when the game has none (independent of <see cref="Dlss"/>).</summary>
    public bool AddMissingDlss { get; init; }
    /// <summary>DLSS release to install; null = latest (never downgrades a newer game DLL).</summary>
    public string? DlssTag { get; init; }
    public bool Streamline { get; init; }
    /// <summary>False = fresh OptiScaler.ini from the release (old one backed up).</summary>
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

    /// <summary>
    /// The same install with OptiScaler-NR and MFG Unlock held at the versions the game already has, for automatic
    /// reinstalls (a Seaglass launch) that must not pull in a release nobody chose. A component whose installed
    /// version is no longer cached is left as it is.
    /// </summary>
    public InstallOptions KeepingInstalledVersions(InstallManifest m) => this with
    {
        Opti = Opti && m.OptiTag is { } ot && ComponentStore.IsCached(Component.OptiScaler, ot),
        OptiTag = m.OptiTag,
        Mfg = Mfg && m.MfgTag is { } mt && ComponentStore.HasMfg(mt),
        MfgTag = m.MfgTag,
        DlssNrId = m.DlssNrId,
    };
}

public sealed class NeedsAdminException(string path)
    : Exception($"No write access to {path}. Restart DLSS Updater as administrator.");

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
        if (o.ReShadeOnly && !o.ReShade) throw new InvalidOperationException("ReShade-only mode installs ReShade as the proxy; ReShade can't be left out.");
        if (!AppSettings.IsKnownProxy(o.Proxy)) throw new ArgumentException($"\"{o.Proxy}\" is not a proxy name DLSS Updater installs under.", nameof(o));
        EnsureNoDoubleMfgUnlock(targetDir, o);
        // Fetch everything first so a network failure never leaves a half-installed game.
        var pkg = o.Opti && !o.ReShadeOnly ? await store.EnsureOptiAsync(o.OptiTag, progress, ct) : null;
        var dlssNr = o.DlssNr;
        var nrPath = dlssNr ? store.VerifiedDlssNrPath(o.DlssNrId ?? store.SelectedDlssNr()) : null;
        if (o.RenoDx && !o.ReShadeOnly)
            throw new InvalidOperationException("The RenoDX DLSS add-on only works in ReShade-only mode: with OptiScaler-NR there would be two NR injectors.");
        if (o.RenoDx && (!o.ReShade || !dlssNr))
            throw new InvalidOperationException("RenoDX needs ReShade with add-on support and an imported NR runtime.");
        if (o.RenoDx && !game.Features.HasFlag(GameFeatures.SR))
            throw new InvalidOperationException("RenoDX needs a game with native DLSS.");
        var renoDx = o.RenoDx ? store.RequireRenoDx() : null;
        var mfg = o.Mfg ? await store.EnsureMfgAsync(o.MfgTag, progress, ct) : null;
        var addMissing = o.AddMissingDlss && o.Opti;
        var dlss = o.Dlss || addMissing ? await store.EnsureDlssAsync(o.DlssTag, progress, ct) : null;
        var sl = o.Streamline && game.Streamline.Count > 0 ? await store.EnsureStreamlineAsync(progress, ct) : null;
        if (o.Streamline && game.Streamline.Count == 0) Log.Info($"{game.Name}: no Streamline files in this game, skipped");
        var reshade = o.ReShade ? await store.EnsureReShadeFileAsync(progress, ct) : null;
        if (o.RenoDx && (reshade is null || FileUtil.ReadVersion(reshade) is not { Major: >= 6 }))
            throw new InvalidOperationException("RenoDX needs ReShade 6 or newer with full add-on support.");

        progress?.Report(new TransferProgress($"Installing to {game.Name}", null));
        // An unreadable or unsafe record stops the install instead of starting over and burying the originals.
        var m = InstallManifest.Read(targetDir) ?? new InstallManifest();
        m.Contain(targetDir, game.Root);
        var ctx = new Ctx(FileUtil.Normalize(game.Root), targetDir, m);

        await Task.Run(() =>
        {
            try
            {
                if (o.ReShadeOnly) RemoveOpti(ctx, o.Proxy, keepNr: dlssNr);
                else if (m.Mode == InstallMode.ReShadeOnly && (pkg is not null || reshade is not null)) RemoveReShadeProxy(ctx, o.Proxy);
                if (o.ReShadeOnly || pkg is not null) m.Mode = o.Mode;

                // Only a run that sets up the injectors drops the add-on; a DLSS-only update keeps it.
                if (!o.RenoDx && m.RenoDx && (o.ReShade || pkg is not null)) RemoveRenoDx(ctx);

                if (pkg is not null)
                {
                    InstallOpti(ctx, pkg, o);
                    if (dlssNr || m.DlssNr) DisplaceForeignNrAddons(ctx, ComponentStore.RenoDxFile, "renodx-dlss5.addon64");
                    m.Opti = true;
                    m.OptiTag = o.OptiTag ?? store.Opti?.Tag;
                    m.Proxy = o.Proxy;
                }
                if (reshade is not null)
                {
                    WriteReShadeIni(ctx, o);
                    // ReShade-only: ReShade itself is the proxy the game loads; otherwise OptiScaler loads ReShade64.dll.
                    Place(ctx, reshade, Path.Combine(targetDir, o.ReShadeOnly ? o.Proxy : ComponentStore.ReShadeFile));
                    if (o.ReShadeOnly)
                    {
                        m.Proxy = o.Proxy;
                        Log.Info($"  {o.Proxy} ← ReShade (no OptiScaler)");
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
                    foreach (var name in ComponentStore.ForeignNrAddons)
                    {
                        var other = Path.Combine(targetDir, name);
                        if (m.Owns(ctx.Rel(other))) { RemoveOwned(ctx, other); Log.Info($"  removed {ctx.Rel(other)} (replaced by {ComponentStore.RenoDxFile})"); }
                        else if (File.Exists(other)) Displace(ctx, other);
                    }
                    Place(ctx, renoDx, Path.Combine(targetDir, ComponentStore.RenoDxFile));
                    m.RenoDx = true;
                    m.RenoDxTag = store.RenoDxTag;
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
            catch (IOException ex) when (IsSharingViolation(ex)) { throw new IOException($"A game file is in use. Close {game.Name} and retry.", ex); }
            finally
            {
                if (!m.IsEmpty) m.Save(targetDir);
            }
        }, ct);
    }

    private static void InstallOpti(Ctx ctx, string pkg, InstallOptions o)
    {
        var m = ctx.M;

        // Other proxies that are OptiScaler, plus files the fork no longer uses.
        foreach (var name in AppSettings.ProxyNames.Where(n => !n.Equals(o.Proxy, StringComparison.OrdinalIgnoreCase)))
        {
            var f = Path.Combine(ctx.Target, name);
            if (File.Exists(f) && IsOptiScaler(f)) Displace(ctx, f);
        }
        foreach (var name in LegacyFiles)
        {
            var f = Path.Combine(ctx.Target, name);
            if (File.Exists(f)) Displace(ctx, f);
        }

        Place(ctx, Path.Combine(pkg, "OptiScaler.dll"), Path.Combine(ctx.Target, o.Proxy));

        foreach (var dir in PackageDirs)
        {
            var src = Path.Combine(pkg, dir);
            if (!Directory.Exists(src)) continue;
            var dest = Path.Combine(ctx.Target, dir);
            var owned = m.Dirs.Contains(ctx.Rel(dest), StringComparer.OrdinalIgnoreCase);
            if (owned || !Directory.Exists(dest))
            {
                MirrorDir(src, dest);
                m.AddDir(ctx.Rel(dest));
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
        if (currentIni is not null && m.Owns(ctx.Rel(iniPath)) && m.OptiIni.Count == 0)
        {
            // Installs from 1.0.0 didn't record what they wrote; values still equal to the profile are ours.
            var cur = IniFile.Parse(currentIni);
            foreach (var ov in profile)
                if (string.Equals(cur.Get(ov.Section, ov.Key), ov.Value, StringComparison.OrdinalIgnoreCase)) m.OptiIni[ov.Id] = ov.Value;
        }
        var merged = ConfigProfile.Merge(releaseIni, currentIni, profile, o.CarryOverIni, m.OptiIni, o.OverwriteIni);
        FileUtil.AtomicWriteText(iniPath, merged.Text);
        m.OptiIni = merged.Applied;
        m.AddFile(ctx.Rel(iniPath));
        Log.Info($"  {o.Proxy} ← OptiScaler-NR, OptiScaler.ini merged");
    }

    /// <summary>
    /// ReShade.ini sits next to ReShade64.dll. It is only patched: keys ReShade or the user wrote stay,
    /// our profile keys are added or updated (unless the user changed them since we last wrote them).
    /// </summary>
    private static void WriteReShadeIni(Ctx ctx, InstallOptions o)
    {
        var path = Path.Combine(ctx.Target, "ReShade.ini");
        var rel = ctx.Rel(path);
        var exists = File.Exists(path);
        var original = exists ? File.ReadAllText(path) : null;
        var profile = o.ReShadeOverrides
            .Where(x => o.RenoDx || !x.Section.Trim().Equals(RenoDxSection, StringComparison.OrdinalIgnoreCase)).ToList();
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
        var hadStale = DropWrittenKeys(cleaned, ctx.M.ReShadeIni, LegacyRenoDxSection);
        if (profile.Count == 0 && ctx.M.ReShadeIni.Count == 0 && !hadStale) return;

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
        if (original == merged.Text) { ctx.M.ReShadeIni = merged.Applied; return; }

        if (exists && !ctx.M.Owns(rel) && ctx.M.BackupOf(rel) is null) Backup(ctx, path, "file", copy: true);
        FileUtil.AtomicWriteText(path, merged.Text);
        ctx.M.ReShadeIni = merged.Applied;
        if (!exists) ctx.M.AddFile(rel);
    }

    private const string RenoDxSection = "RENODX-DLSS";
    /// <summary>The section of a different add-on (renodx-dlss5) that older versions of this app wrote by mistake.</summary>
    private const string LegacyRenoDxSection = "RenoDX.DLSS5";

    private static List<string> EarlyAddons(IniFile ini) =>
        (ini.Get("ADDON", "LoadFromDllMain") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Removes keys of <paramref name="section"/> that still hold the value we wrote, and forgets all we wrote there.</summary>
    private static bool DropWrittenKeys(IniFile ini, Dictionary<string, string> applied, string section)
    {
        var dropped = false;
        foreach (var (id, value) in applied.ToList())
        {
            var slash = id.IndexOf('/');
            if (slash <= 0 || !id[..slash].Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            var key = id[(slash + 1)..];
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
            changed |= DropWrittenKeys(ini, m.ReShadeIni, RenoDxSection);
            if (changed) ini.Save(path);
        }
        m.RenoDx = false;
        m.RenoDxTag = null;
        Log.Info($"  {ComponentStore.RenoDxFile} removed");
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

    /// <summary>
    /// OptiScaler-NR v0.9+ can unlock MFG itself ([DLSSG] AdaMfgUnlock / AmpereMfgUnlock) and says never to combine that
    /// with an external unlocker, which the MFG Unlock add-on is. Checked before anything is fetched or written.
    /// </summary>
    private static void EnsureNoDoubleMfgUnlock(string targetDir, InstallOptions o)
    {
        if (!o.Mfg || o.ReShadeOnly) return;
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
            var entry = m.BackupOf(rel);
            var ours = entry?.InstalledSha is { } s && s == HashCache.Get(dll.Path);
            if (!ours) Backup(ctx, dll.Path, "streamline");
            FileUtil.AtomicCopy(src, dll.Path);
            if (m.BackupOf(rel) is { } b) b.InstalledSha = HashCache.Get(src);
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

        foreach (var dll in replace ? scan.Dlss : [])
        {
            if (FileUtil.IsUnder(dll.Path, InstallManifest.DirFor(ctx.Target))) continue;
            if (!latest.TryGetValue(dll.Name, out var src)) continue;
            var newVer = FileUtil.ReadVersion(src);
            var rel = ctx.Rel(dll.Path);
            // Latest: only ever upgrade. Pinned: move to exactly that version, up or down.
            if (dll.Version is not null && newVer is not null && (exact ? dll.Version == newVer : dll.Version >= newVer)) continue;

            var srcSha = HashCache.Get(src);
            var entry = m.BackupOf(rel);
            var ours = m.Owns(rel) || (entry?.InstalledSha is { } s && s == HashCache.Get(dll.Path));
            if (!ours) Backup(ctx, dll.Path, "dlss");
            FileUtil.AtomicCopy(src, dll.Path);
            if (m.BackupOf(rel) is { } b) b.InstalledSha = srcSha;
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

    /// <summary>Puts <paramref name="src"/> at <paramref name="dest"/>, backing up anything we don't own.</summary>
    private static void Place(Ctx ctx, string src, string dest)
    {
        var rel = ctx.Rel(dest);
        if (File.Exists(dest))
        {
            if (ctx.M.Owns(rel) && FileUtil.SameFile(src, dest)) return;
            if (!ctx.M.Owns(rel)) Backup(ctx, dest, "file");
        }
        FileUtil.AtomicCopy(src, dest);
        ctx.M.AddFile(rel);
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
        if (m.Opti || m.DlssNr) Log.Info("  OptiScaler-NR removed (ReShade-only)");
        m.Opti = false;
        m.OptiTag = null;
        m.OptiIni.Clear();
        if (!keepNr) { m.DlssNr = false; m.DlssNrSha = null; m.DlssNrId = null; }
    }

    /// <summary>Switching back from ReShade-only: the ReShade proxy goes unless OptiScaler takes over the same name.</summary>
    private static void RemoveReShadeProxy(Ctx ctx, string newProxy)
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
        FileUtil.TryDeleteEmptyDir(Path.GetDirectoryName(full)!);
    }

    /// <summary>Removes a conflicting file: deleted if we placed it, otherwise moved to the backup.</summary>
    private static void Displace(Ctx ctx, string full)
    {
        var rel = ctx.Rel(full);
        if (ctx.M.Owns(rel))
        {
            File.Delete(full);
            ctx.M.Files.RemoveAll(f => f.Equals(rel, StringComparison.OrdinalIgnoreCase));
        }
        else Backup(ctx, full, "file");
        Log.Info($"  removed {rel}");
    }

    private static void Backup(Ctx ctx, string full, string kind, bool copy = false)
    {
        var rel = ctx.Rel(full);
        var entry = ctx.M.BackupOf(rel);
        var backupRel = entry?.Backup ?? FreeBackupName(ctx, BackupName(rel));
        var dest = Path.Combine(ctx.BackupDir, backupRel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (copy) File.Copy(full, dest, true);
        else File.Move(full, dest, true);
        if (entry is null) ctx.M.Backups.Add(new BackupEntry { Original = rel, Backup = backupRel, Kind = kind });
        else entry.InstalledSha = null;
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
        var target = Path.Combine(ctx.Target, ComponentStore.DlssNrFile);
        var selected = store.RetainDlssNr(source);
        var previous = File.Exists(target) ? store.RetainDlssNr(target) : null;
        Place(ctx, source, target);
        if (previous is not null && previous.Id != selected.Id) ctx.M.PreviousDlssNrId = previous.Id;
        ctx.M.DlssNr = true;
        ctx.M.DlssNrId = selected.Id;
        ctx.M.DlssNrSha = selected.Sha256;
    }

    public Task SwitchDlssNrAsync(GameInfo game, string targetDir, string? id, CancellationToken ct) => Task.Run(() =>
    {
        targetDir = FileUtil.Normalize(targetDir);
        EnsureNotRunning(game.Root);
        EnsureWritable(targetDir);
        var m = InstallManifest.Read(targetDir) ?? throw new InvalidOperationException("Install NR in this game first.");
        m.Contain(targetDir, game.Root);
        if (!m.DlssNr) throw new InvalidOperationException("Install NR in this game first.");
        var ctx = new Ctx(FileUtil.Normalize(game.Root), targetDir, m);
        var source = store.VerifiedDlssNrPath(id ?? store.SelectedDlssNr());
        var target = Path.Combine(targetDir, ComponentStore.DlssNrFile);
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
            else File.Delete(target);
            throw;
        }
    }, ct);

    // ---------- uninstall / restore ----------

    public Task UninstallAsync(GameInfo game, string targetDir, CancellationToken ct) => Task.Run(() =>
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
                TryDelete(ctx.Full(rel));
                m.Files.Remove(rel);
                m.Save(targetDir);
            }
            foreach (var rel in m.Dirs.ToList())
            {
                var d = ctx.Full(rel);
                if (Directory.Exists(d)) Directory.Delete(d, true);
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
            Directory.Delete(InstallManifest.DirFor(targetDir), true);
            Log.Info($"{game.Name}: uninstalled, originals restored");
        }
        catch (UnauthorizedAccessException) { throw new NeedsAdminException(targetDir); }
    }, ct);

    public Task RestoreDlssAsync(GameInfo game, string targetDir, CancellationToken ct) => Task.Run(() =>
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
            foreach (var rel in m.Files.Where(f => ComponentStore.DlssFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)).ToList())
            {
                TryDelete(ctx.Full(rel));
                m.Files.Remove(rel);
            }
            m.Dlss = false;
            m.DlssTag = null;
            m.DlssPinned = false;
            m.Streamline = false;
            m.StreamlineTag = null;
            if (m.IsEmpty) Directory.Delete(InstallManifest.DirFor(targetDir), true);
            else m.Save(targetDir);
            Log.Info($"{game.Name}: original DLSS and Streamline files restored");
        }
        catch (UnauthorizedAccessException) { throw new NeedsAdminException(targetDir); }
    }, ct);

    private static void Restore(Ctx ctx, BackupEntry b)
    {
        var src = Path.Combine(ctx.BackupDir, b.Backup);
        if (!File.Exists(src)) return;
        var dest = ctx.Full(b.Original);
        // A game patch replaced the file we put in: the game's newer file stays and the stale original goes.
        if (b.InstalledSha is { } ours && File.Exists(dest) && !string.Equals(HashCache.Get(dest), ours, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(src);
            Log.Info($"  {b.Original}: changed by the game since, kept");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Move(src, dest, true);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

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

/// <summary>SHA-256 memo keyed by path + size + mtime; big dlls get hashed once per session.</summary>
public static class HashCache
{
    private static readonly Dictionary<(string, long, DateTime), string> Cache = new();

    public static string Get(string path)
    {
        var fi = new FileInfo(path);
        var key = (fi.FullName.ToLowerInvariant(), fi.Length, fi.LastWriteTimeUtc);
        lock (Cache)
            if (Cache.TryGetValue(key, out var h)) return h;
        var hash = FileUtil.Sha256(path);
        lock (Cache) Cache[key] = hash;
        return hash;
    }
}
