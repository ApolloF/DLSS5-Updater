namespace DLSSUpdater.Core;

/// <summary>
/// Reads the components a record keeps in its per-component fields (every record before 3.0, and the components that
/// still install the old way) as <see cref="InstalledComponent"/>s, so code that works by component id sees them too.
/// The fields stay where they are and remain what the installer reads and writes, which also keeps the record readable
/// by 2.2.x. A record file attributes nothing it can't place; entries no component claims stay in the record-level lists,
/// which uninstall and restore go by as before.
/// </summary>
public static class LegacyComponents
{
    /// <summary>Stands in for the install folder of a record that wasn't read from disk; only its own entries then place beside it.</summary>
    private static readonly string Unknown = Path.Combine(Path.GetTempPath(), "dlssu-legacy-view");

    public static List<InstalledComponent> Project(InstallManifest m)
    {
        // Which entries lie beside the game exe can only be told from the folder the record belongs to.
        var known = m.ReadFrom ?? m.Dir;
        var target = FileUtil.Normalize(known ?? Unknown);
        string root;
        try { root = FileUtil.Normalize(Path.Combine(target, m.RootRel)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { root = target; }

        // Each record-level entry, by where it lies relative to the install folder; null when it can't be placed there.
        string? Local(string entry)
        {
            try
            {
                var full = InstallManifest.Resolve(entry, target, root);
                if (FileUtil.IsUnder(full, target)) return Path.GetRelativePath(target, full);
                // Without the folder, a game-root entry is taken by its name: what the old components placed sits beside the exe.
                return known is null && !Path.IsPathRooted(entry) ? Path.GetFileName(entry) : null;
            }
            catch (Exception ex) when (ex is UnsafeManifestException or ArgumentException or NotSupportedException) { return null; }
        }
        string Name(string entry) => Path.GetFileName(entry.Replace('/', '\\'));
        string EntryOf(string local)
        {
            if (known is null) return local;
            try { return InstallManifest.Entry(Path.Combine(target, local), target, root); }
            catch (UnsafeManifestException) { return local; }
        }

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<InstalledComponent>();
        InstalledComponent Add(string id, string? build, string? version, Func<string, bool> owns, Func<BackupEntry, bool>? ownsBackup = null)
        {
            var c = new InstalledComponent { Id = id, BuildId = build, Version = version, Legacy = true, Installed = m.Updated };
            foreach (var f in m.Files.Where(f => !claimed.Contains(f) && owns(f)))
            {
                c.Files.Add(new PlacedFile { Path = f });
                claimed.Add(f);
            }
            foreach (var b in m.Backups.Where(b => c.FileAt(b.Original) is not null || ownsBackup?.Invoke(b) == true))
                c.Backups.TryAdd(b.Original, b.Backup);
            result.Add(c);
            return c;
        }
        void Edits(InstalledComponent c, Dictionary<string, string> applied, string file, Func<string, bool>? section = null)
        {
            foreach (var (id, value) in applied)
            {
                var slash = id.IndexOf('/');
                if (slash <= 0 || section?.Invoke(id[..slash]) == false) continue;
                c.ConfigEdits.Add(new ConfigEdit { File = EntryOf(file), Section = id[..slash], Key = id[(slash + 1)..], Value = value, PreviousUnknown = true });
            }
        }
        bool Beside(string entry, params string[] names) => Local(entry) is { } l && names.Contains(l, StringComparer.OrdinalIgnoreCase);
        void Hash(InstalledComponent c, string local, string? sha)
        {
            if (sha is not null && c.FileAt(EntryOf(local)) is { } f) f.Sha256 = sha;
        }
        var reShadeSections = new[] { RenoDxIni.Section, RenoDxIni.PresetSection, LecramIni.Section, UpliftConfig.Section };
        bool OtherSection(string s) => reShadeSections.Contains(s, StringComparer.OrdinalIgnoreCase);

        // Files placed for one component under another's name go first: DFC's runtimes are DLSS files, the NR runtime may sit in Uplift's folder.
        if (m.Dfc || m.DfcFiles.Count > 0 || m.DfcRuntimes.Count > 0 || m.DfcCfg.Count > 0)
        {
            var dfc = Add(ComponentRegistry.Dfc, m.DfcId ?? m.DfcTag, m.DfcTag,
                f => m.DfcFiles.Contains(f, StringComparer.OrdinalIgnoreCase) || m.DfcRuntimes.Contains(f, StringComparer.OrdinalIgnoreCase)
                     || Local(f) is { } l && (ComponentStore.IsDfcPackagePath(l) || l.Equals(ComponentStore.DfcConfigFile, StringComparison.OrdinalIgnoreCase)),
                b => Beside(b.Original, ComponentStore.DfcConfigFile));
            Edits(dfc, m.DfcCfg, ComponentStore.DfcConfigFile);
            if (m.DfcRuntimes.Count > 0) dfc.Extras["Runtimes"] = string.Join('|', m.DfcRuntimes);
        }
        if (m.DlssNr)
        {
            var file = m.DlssNrFile ?? ComponentStore.DlssNrFile;
            var nr = Add(ComponentRegistry.DlssNr, m.DlssNrId, null, f => Beside(f, file));
            Hash(nr, file, m.DlssNrSha);
            if (m.DlssNrFile is not null) nr.Extras["File"] = m.DlssNrFile;
            if (m.PreviousDlssNrId is not null) nr.Extras["PreviousId"] = m.PreviousDlssNrId;
        }
        if (m.Opti)
        {
            var proxy = m.Mode == InstallMode.OptiScaler ? m.Proxy : null;
            var extras = OptiSources.All.SelectMany(s => s.ExtraFiles).ToArray();
            var opti = Add(ComponentRegistry.OptiScaler, m.OptiTag, m.OptiTag,
                f => Local(f) is { } l && (l.Equals(proxy, StringComparison.OrdinalIgnoreCase) || extras.Contains(l, StringComparer.OrdinalIgnoreCase)
                                           || ComponentRegistry.Find(ComponentRegistry.OptiScaler)!.OwnsFile(l)));
            Edits(opti, m.OptiIni, "OptiScaler.ini");
            if (proxy is not null) opti.Extras["Proxy"] = proxy;
        }
        if (m.ReShade)
        {
            var file = m.Mode == InstallMode.ReShadeOnly && m.Proxy is not null ? m.Proxy : ComponentStore.ReShadeFile;
            var reshade = Add(ComponentRegistry.ReShade, m.ReShadeSha, null, f => Beside(f, file, "ReShade.ini"));
            Hash(reshade, file, m.ReShadeSha);
            Edits(reshade, m.ReShadeIni, "ReShade.ini", s => !OtherSection(s));
            if (m.Mode == InstallMode.ReShadeOnly && m.Proxy is not null) reshade.Extras["Proxy"] = m.Proxy;
        }
        if (m.RenoDx)
        {
            var renoDx = Add(ComponentRegistry.RenoDx, m.RenoDxId ?? m.RenoDxTag, m.RenoDxTag,
                f => Beside(f, [ComponentStore.RenoDxFile, .. ComponentStore.LegacyRenoDxFiles]));
            Edits(renoDx, m.ReShadeIni, "ReShade.ini", s => s.Equals(RenoDxIni.Section, StringComparison.OrdinalIgnoreCase) || s.Equals(RenoDxIni.PresetSection, StringComparison.OrdinalIgnoreCase));
        }
        if (m.Lecram)
        {
            var lecram = Add(ComponentRegistry.Lecram, m.LecramId ?? m.LecramTag, m.LecramTag, f => Beside(f, ComponentStore.LecramFiles));
            Edits(lecram, m.ReShadeIni, "ReShade.ini", s => s.Equals(LecramIni.Section, StringComparison.OrdinalIgnoreCase));
            if (m.LecramFile is not null) lecram.Extras["File"] = m.LecramFile;
        }
        if (m.Uplift)
        {
            var uplift = Add(ComponentRegistry.Uplift, m.UpliftTag, m.UpliftTag, f => Beside(f, ComponentStore.UpliftFiles));
            Edits(uplift, m.ReShadeIni, "ReShade.ini", s => s.Equals(UpliftConfig.Section, StringComparison.OrdinalIgnoreCase));
        }
        if (m.Mfg) Add(ComponentRegistry.MfgUnlock, m.MfgTag, m.MfgTag, f => Beside(f, ComponentStore.MfgFile));
        if (m.Streamline || m.Backups.Any(b => b.Kind == "streamline"))
        {
            var sl = Add(ComponentRegistry.Streamline, m.StreamlineTag, m.StreamlineTag,
                f => m.BackupOf(f)?.Kind == "streamline" || Name(f).StartsWith("sl.", StringComparison.OrdinalIgnoreCase) && Name(f).EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
                b => b.Kind == "streamline");
            foreach (var f in sl.Files) f.Sha256 = m.BackupOf(f.Path)?.InstalledSha;
        }
        if (m.Dlss || m.Backups.Any(b => b.Kind == "dlss") || m.Files.Any(f => !claimed.Contains(f) && ComponentStore.DlssFiles.Contains(Name(f), StringComparer.OrdinalIgnoreCase)))
        {
            var dlss = Add(ComponentRegistry.Dlss, m.DlssTag, m.DlssTag,
                f => m.BackupOf(f)?.Kind == "dlss" || ComponentStore.DlssFiles.Contains(Name(f), StringComparer.OrdinalIgnoreCase),
                b => b.Kind == "dlss");
            foreach (var f in dlss.Files) f.Sha256 = m.BackupOf(f.Path)?.InstalledSha;
            if (m.DlssPinned) dlss.Extras["Pinned"] = "true";
        }
        return result;
    }
}
