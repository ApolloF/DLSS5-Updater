using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>
/// Install and removal of components recorded the generic way (<see cref="InstalledComponent"/>). Every step is recorded
/// before it is taken, and removal undoes exactly what the record lists, in reverse order: attributes, config keys, then
/// files with their originals put back. Whatever changed since we made it (a file, a key, an attribute) stays as it is and
/// is reported.
/// </summary>
public sealed partial class Installer
{
    /// <summary>Brings one registered component in the game to <paramref name="plan"/>, from whatever the record says it has now.</summary>
    /// <returns>What a replaced build left behind because it changed since we placed it.</returns>
    public Task<ComponentUndo> InstallComponentAsync(GameInfo game, string targetDir, ComponentPlan plan, CancellationToken ct) =>
        Demo.Active ? Task.FromResult(new ComponentUndo(plan.Id, [], [], [])) : Task.Run(() =>
        {
            targetDir = FileUtil.Normalize(targetDir);
            EnsureNotRunning(game.Root);
            EnsureWritable(targetDir);
            try
            {
                var m = InstallManifest.Read(targetDir) ?? new InstallManifest();
                m.Contain(targetDir, game.Root);
                var ctx = new Ctx(FileUtil.Normalize(game.Root), targetDir, m);
                try
                {
                    var undo = ApplyPlan(ctx, plan);
                    Log.Info($"{game.Name}: {ComponentRegistry.Find(plan.Id)?.Name ?? plan.Id} {plan.Version} installed");
                    return undo;
                }
                catch (UnauthorizedAccessException) { throw new NeedsAdminException(targetDir); }
                catch (IOException ex) when (IsSharingViolation(ex)) { throw new GameFileInUseException(game.Name, ex); }
                finally
                {
                    if (!m.IsEmpty) m.Save(targetDir);
                }
            }
            finally
            {
                FileUtil.TryDeleteEmptyDir(InstallManifest.DirFor(targetDir));
            }
        }, ct);

    /// <summary>Takes one component out of the game and puts back what it replaced; the rest of the install stays.</summary>
    public Task<ComponentUndo> RemoveComponentAsync(GameInfo game, string targetDir, string id, CancellationToken ct) =>
        Demo.Active ? Task.FromResult(new ComponentUndo(id, [], [], [])) : Task.Run(() =>
        {
            targetDir = FileUtil.Normalize(targetDir);
            EnsureNotRunning(game.Root);
            var m = InstallManifest.Read(targetDir) ?? throw new InvalidOperationException("No DLSS5 Updater install found here.");
            m.Contain(targetDir, game.Root);
            var c = m.Component(id) ?? throw new InvalidOperationException($"{ComponentRegistry.Find(id)?.Name ?? id} is not installed here.");
            var ctx = new Ctx(FileUtil.Normalize(game.Root), targetDir, m);
            try
            {
                var undo = UndoComponent(ctx, c);
                if (m.IsEmpty) FileUtil.DeleteDirectory(InstallManifest.DirFor(targetDir));
                Log.Info($"{game.Name}: {ComponentRegistry.Find(id)?.Name ?? id} removed");
                return undo;
            }
            catch (UnauthorizedAccessException) { throw new NeedsAdminException(targetDir); }
            catch (IOException ex) when (IsSharingViolation(ex)) { throw new GameFileInUseException(game.Name, ex); }
        }, ct);

    /// <summary>
    /// The plan's files, keys and attributes in place, and those the component had and the plan drops undone. Components
    /// that keep their own fields in the record (<see cref="ComponentDefinition.Legacy"/>) install through their own code.
    /// </summary>
    private static ComponentUndo ApplyPlan(Ctx ctx, ComponentPlan plan)
    {
        var def = ComponentRegistry.Find(plan.Id) ?? throw new InvalidOperationException($"\"{plan.Id}\" is not a registered component.");
        if (def.Legacy) throw new InvalidOperationException($"{def.Name} installs through its own steps, not a component plan.");
        var m = ctx.M;
        var c = m.Component(def.Id);
        // The whole plan is checked before anything is recorded or changed.
        var files = plan.Files.Select(f => (f.Source, Dest: FileUtil.Normalize(f.Dest), Entry: ctx.Rel(FileUtil.Normalize(f.Dest)))).ToList();
        foreach (var f in files)
        {
            if (!File.Exists(f.Source)) throw new FileNotFoundException($"{def.Name}: {f.Source} is missing from the build.", f.Source);
            if (m.Components.FirstOrDefault(o => o != c && o.FileAt(f.Entry) is not null) is { } other)
                throw new InvalidOperationException($"{f.Entry} belongs to {ComponentRegistry.Find(other.Id)?.Name ?? other.Id}; remove that first.");
        }
        var edits = plan.ConfigEdits.Select(e => (Entry: ConfigEntryOf(ctx, e.File), e.Section, e.Key, e.Value)).ToList();
        var readOnly = plan.ReadOnly.Select(p => ConfigEntryOf(ctx, p)).ToList();

        if (c is null)
        {
            c = new InstalledComponent { Id = def.Id };
            m.Components.Add(c);
        }
        c.BuildId = plan.BuildId;
        c.Version = plan.Version;
        c.Installed = DateTime.UtcNow;
        c.Extras = new Dictionary<string, string>(plan.Extras, StringComparer.OrdinalIgnoreCase);
        Persist(ctx);

        var report = new UndoReport();
        // What the plan no longer has goes first, in reverse order of how it was made.
        foreach (var a in Enumerable.Reverse(c.Attributes).Where(a => !readOnly.Contains(a.Path, StringComparer.OrdinalIgnoreCase)).ToList())
            UndoAttributes(ctx, c, a, report);
        var dropped = c.ConfigEdits.Where(e => !edits.Any(k => k.Entry.Equals(e.File, StringComparison.OrdinalIgnoreCase)
                                                            && k.Section.Equals(e.Section, StringComparison.OrdinalIgnoreCase)
                                                            && k.Key.Equals(e.Key, StringComparison.OrdinalIgnoreCase))).ToList();
        UndoConfigEdits(ctx, c, dropped, report);
        foreach (var f in Enumerable.Reverse(c.Files).Where(f => !files.Any(w => w.Entry.Equals(f.Path, StringComparison.OrdinalIgnoreCase))).ToList())
            UndoFile(ctx, c, f, report);

        foreach (var (src, dest, _) in files) PlaceComponentFile(ctx, c, src, dest);
        foreach (var group in edits.GroupBy(e => e.Entry, StringComparer.OrdinalIgnoreCase))
            SetConfigKeys(ctx, c, group.Key, group.Select(e => (e.Section, e.Key, e.Value)).ToList());
        foreach (var entry in readOnly) SetReadOnly(ctx, c, entry);
        Persist(ctx);
        return report.For(c.Id);
    }

    /// <summary>Removes a component completely: attributes back, keys back, files out and originals back, then its entry.</summary>
    private static ComponentUndo UndoComponent(Ctx ctx, InstalledComponent c)
    {
        var report = new UndoReport();
        foreach (var a in Enumerable.Reverse(c.Attributes).ToList()) UndoAttributes(ctx, c, a, report);
        UndoConfigEdits(ctx, c, c.ConfigEdits.ToList(), report);
        foreach (var f in Enumerable.Reverse(c.Files).ToList()) UndoFile(ctx, c, f, report);
        // Originals of files that left by another way (a later install displaced them) still go back.
        foreach (var original in c.Backups.Keys.ToList())
        {
            if (ctx.M.BackupOf(original) is { } b && !ctx.M.Owns(original))
            {
                Restore(ctx, b);
                ctx.M.Backups.Remove(b);
            }
            c.Backups.Remove(original);
        }
        ctx.M.Components.Remove(c);
        Persist(ctx);
        return report.For(c.Id);
    }

    private sealed class UndoReport
    {
        public List<string> Files { get; } = [];
        public List<string> Keys { get; } = [];
        public List<string> Attributes { get; } = [];
        public ComponentUndo For(string id) => new(id, Files, Keys, Attributes);
    }

    /// <summary>A config or attribute target as the record names it; inside the game or install folder, or in a known folder.</summary>
    private static string ConfigEntryOf(Ctx ctx, string full)
    {
        full = FileUtil.Normalize(full);
        var entry = InstallManifest.ConfigEntry(full, ctx.Target, ctx.Root);
        EnsureNoLink(ctx, entry, full);
        return entry;
    }

    private static string ConfigFull(Ctx ctx, string entry)
    {
        var full = InstallManifest.Resolve(entry, ctx.Target, ctx.Root);
        // Resolving and recording again must give the same entry: anything else lies outside the places we may change.
        if (!InstallManifest.ConfigEntry(full, ctx.Target, ctx.Root).Equals(entry, StringComparison.OrdinalIgnoreCase))
            throw new UnsafeManifestException(ctx.Target, $"\"{entry}\"");
        EnsureNoLink(ctx, entry, full);
        return full;
    }

    private static void EnsureNoLink(Ctx ctx, string entry, string full)
    {
        var tag = InstallManifest.KnownFolderTags.FirstOrDefault(t => entry.StartsWith(t, StringComparison.Ordinal));
        var root = tag is not null ? InstallManifest.KnownFolder(tag)! : FileUtil.IsUnder(full, ctx.Root) ? ctx.Root : ctx.Target;
        if (FileUtil.LinkBelow(root, full) is { } link) throw new UnsafeManifestException(ctx.Target, $"{link} is a link");
    }

    /// <summary>
    /// Places one file of the component, backing up what was there unless it is already ours. A file of ours that changed
    /// since (by the user or the game) is kept in .dlssupdater\previous before the new build replaces it.
    /// </summary>
    private static void PlaceComponentFile(Ctx ctx, InstalledComponent c, string src, string dest)
    {
        var m = ctx.M;
        dest = FileUtil.Normalize(dest);
        var rel = ctx.Rel(dest);
        if (FileUtil.LinkBelow(FileUtil.IsUnder(dest, ctx.Root) ? ctx.Root : ctx.Target, dest) is { } link)
            throw new UnsafeManifestException(ctx.Target, $"{link} is a link");
        if (m.Components.FirstOrDefault(o => o != c && o.FileAt(rel) is not null) is { } other)
            throw new InvalidOperationException($"{rel} belongs to {ComponentRegistry.Find(other.Id)?.Name ?? other.Id}; remove that first.");
        var sha = FileUtil.Sha256(src);
        var placed = c.FileAt(rel);
        var exists = File.Exists(dest);
        if (placed is not null && exists && m.Owns(rel))
        {
            var now = FileUtil.Sha256(dest);
            if (now.Equals(sha, StringComparison.OrdinalIgnoreCase))
            {
                placed.Sha256 = sha;
                return;
            }
            if (placed.Sha256 is { } ours && !now.Equals(ours, StringComparison.OrdinalIgnoreCase))
            {
                KeepPrevious(ctx, dest);
                Log.Warn($"  {rel}: changed since it was installed (by you or the game); that copy is kept in {Path.Combine(InstallManifest.DirName, PreviousDir)}");
            }
        }
        var isNew = placed is null;
        var oldSha = placed?.Sha256;
        placed ??= new PlacedFile { Path = rel };
        if (isNew) c.Files.Add(placed);
        placed.Sha256 = sha;
        var hadBackup = m.BackupOf(rel) is not null;
        try
        {
            // Replace saves the record (with this entry) before the file goes in.
            Replace(ctx, src, dest, m.Owns(rel) ? null : "file", own: true);
        }
        catch
        {
            if (isNew) c.Files.Remove(placed);
            else placed.Sha256 = oldSha;
            TryPersist(ctx);
            throw;
        }
        if (!hadBackup && m.BackupOf(rel) is { } b)
        {
            c.Backups[rel] = b.Backup;
            Persist(ctx);
        }
    }

    /// <summary>A placed file goes and its original comes back, unless it changed since: then it stays, and the original we kept for it goes.</summary>
    private static void UndoFile(Ctx ctx, InstalledComponent c, PlacedFile f, UndoReport report)
    {
        var m = ctx.M;
        var full = ctx.Full(f.Path);
        var backup = m.BackupOf(f.Path);
        if (File.Exists(full) && f.Sha256 is { } ours && !FileUtil.Sha256(full).Equals(ours, StringComparison.OrdinalIgnoreCase))
        {
            report.Files.Add(f.Path);
            Log.Warn($"  {f.Path}: changed since it was installed (by you or the game), left in place" +
                     (backup is null ? "" : "; the original it had replaced is not put back over it"));
            if (backup is not null)
            {
                FileUtil.DeleteFile(Path.Combine(ctx.BackupDir, backup.Backup));
                m.Backups.Remove(backup);
            }
        }
        else
        {
            FileUtil.DeleteFile(full);
            if (backup is not null)
            {
                Restore(ctx, backup);
                m.Backups.Remove(backup);
            }
        }
        m.Files.RemoveAll(e => e.Equals(f.Path, StringComparison.OrdinalIgnoreCase));
        c.Files.Remove(f);
        c.Backups.Remove(f.Path);
        DeleteEmptyDirs(ctx, Path.GetDirectoryName(full)!);
        Persist(ctx);
    }

    /// <summary>
    /// Sets keys in one config file. The value a key had before our first change is recorded (and saved) before the file is
    /// written; a key set again keeps that first value.
    /// </summary>
    private static void SetConfigKeys(Ctx ctx, InstalledComponent c, string entry, IReadOnlyList<(string Section, string Key, string Value)> keys)
    {
        var full = ConfigFull(ctx, entry);
        var exists = File.Exists(full);
        var text = exists ? File.ReadAllText(full) : "";
        var ini = IniFile.Parse(text, keepFinalNewlineAsIs: true);
        foreach (var (section, key, value) in keys)
        {
            var e = c.EditOf(entry, section, key);
            if (e is null)
            {
                e = new ConfigEdit { File = entry, Section = section, Key = key, Previous = ini.Get(section, key), FileCreated = !exists, SectionCreated = !ini.HasSection(section) };
                c.ConfigEdits.Add(e);
            }
            else if (!exists) e.FileCreated = true;
            e.Value = value;
            ini.Set(section, key, value);
        }
        Persist(ctx);
        if (ini.ToString() != text) FileUtil.AtomicWriteText(full, Bom(full) + ini.ToString());
    }

    /// <summary>The byte order mark an existing config file starts with, so a rewrite keeps it (Unreal writes some of its ini files with one).</summary>
    private static string Bom(string full)
    {
        if (!File.Exists(full)) return "";
        using var fs = File.OpenRead(full);
        Span<byte> head = stackalloc byte[3];
        return fs.Read(head) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? "﻿" : "";
    }

    /// <summary>
    /// Puts back what the keys held before our first change (removing those that were absent), newest edit first. A key whose
    /// value is no longer the one we wrote changed since: it stays. A file we created goes when no key is left in it.
    /// </summary>
    private static void UndoConfigEdits(Ctx ctx, InstalledComponent c, IReadOnlyList<ConfigEdit> edits, UndoReport report)
    {
        foreach (var group in edits.GroupBy(e => e.File, StringComparer.OrdinalIgnoreCase))
        {
            var full = ConfigFull(ctx, group.Key);
            if (File.Exists(full))
            {
                var text = File.ReadAllText(full);
                var ini = IniFile.Parse(text, keepFinalNewlineAsIs: true);
                foreach (var e in group.Reverse())
                {
                    // IniFile reads values trimmed.
                    if (!string.Equals(ini.Get(e.Section, e.Key), e.Value.Trim(), StringComparison.Ordinal))
                    {
                        report.Keys.Add($"{e.File} [{e.Section}] {e.Key}");
                        Log.Warn($"  {e.File} [{e.Section}] {e.Key}: changed since it was set (by you or the game), left as it is");
                    }
                    else if (e.Previous is null) ini.Remove(e.Section, e.Key);
                    else ini.Set(e.Section, e.Key, e.Previous);
                }
                foreach (var section in group.Where(e => e.SectionCreated).Select(e => e.Section).Distinct(StringComparer.OrdinalIgnoreCase))
                    ini.RemoveSectionIfEmpty(section);
                if (group.Any(e => e.FileCreated) && !ini.Entries().Any()) FileUtil.DeleteFile(full);
                else if (ini.ToString() != text) FileUtil.AtomicWriteText(full, Bom(full) + ini.ToString());
            }
            foreach (var e in group) c.ConfigEdits.Remove(e);
            Persist(ctx);
        }
    }

    private static void SetReadOnly(Ctx ctx, InstalledComponent c, string entry)
    {
        var full = ConfigFull(ctx, entry);
        if (!File.Exists(full))
        {
            Log.Warn($"  {entry}: not there, so it can't be made read-only");
            return;
        }
        var all = File.GetAttributes(full);
        var now = all & AttributeChange.Managed;
        var want = now | FileAttributes.ReadOnly;
        var a = c.AttributesOf(entry);
        if (a is null)
        {
            if (now == want) return;
            a = new AttributeChange { Path = entry, Before = now };
            c.Attributes.Add(a);
        }
        a.After = want;
        Persist(ctx);
        if (now != want) SetManaged(full, all, want);
    }

    /// <summary>The attributes go back to what they were, unless they changed since we set them.</summary>
    private static void UndoAttributes(Ctx ctx, InstalledComponent c, AttributeChange a, UndoReport report)
    {
        var full = ConfigFull(ctx, a.Path);
        if (File.Exists(full))
        {
            var all = File.GetAttributes(full);
            if ((all & AttributeChange.Managed) != a.After)
            {
                report.Attributes.Add(a.Path);
                Log.Warn($"  {a.Path}: attributes changed since they were set (by you or the game), left as they are");
            }
            else SetManaged(full, all, a.Before);
        }
        c.Attributes.Remove(a);
        Persist(ctx);
    }

    private static void SetManaged(string full, FileAttributes all, FileAttributes managed)
    {
        var attributes = (all & ~AttributeChange.Managed) | (managed & AttributeChange.Managed);
        File.SetAttributes(full, attributes == 0 ? FileAttributes.Normal : attributes);
    }
}
