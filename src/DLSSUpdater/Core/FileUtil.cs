using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace DLSSUpdater.Core;

public static class FileUtil
{
    public static Version? ReadVersion(string path)
    {
        if (Demo.Active && Demo.VersionOf(path) is { } demo) return demo;
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            if (v.FileMajorPart == 0 && v.FileMinorPart == 0 && v.FileBuildPart == 0 && v.FilePrivatePart == 0)
                return null;
            return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
        }
        catch (FileNotFoundException) { return null; }
    }

    public static string? OriginalFilename(string path) => VersionInfo(path)?.OriginalFilename?.Trim();

    public static string? InternalName(string path) => VersionInfo(path)?.InternalName?.Trim();

    private static FileVersionInfo? VersionInfo(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path); }
        catch (FileNotFoundException) { return null; }
    }

    /// <summary>310.9.1.0 -> "310.9.1"</summary>
    public static string Format(Version? v)
    {
        if (v is null) return "—";
        if (v.Revision > 0) return v.ToString(4);
        return v.Build >= 0 ? v.ToString(3) : v.ToString(2);
    }

    /// <summary>Parses tags like "v310.9.1", "1.1.5", "v0.8.91".</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim().TrimStart('v', 'V');
        var end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.')) end++;
        s = s[..end].Trim('.');
        if (!s.Contains('.')) s += ".0";
        return Version.TryParse(s, out var v) ? v : null;
    }

    public static string Sha256(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public static bool SameFile(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
        // Content decides: two different builds can share size and timestamp (same install time,
        // files written in the same clock tick), and then a swap would be skipped.
        return HashCache.Get(a) == HashCache.Get(b);
    }

    /// <summary>Copies via a temp file next to the destination, then swaps it in. Preserves timestamps.</summary>
    public static void AtomicCopy(string src, string dest) => CommitStaged(StageCopy(src, dest), dest);

    /// <summary>Copies <paramref name="src"/> to a temp file beside <paramref name="dest"/> for <see cref="CommitStaged"/>; a failed copy leaves no temp file.</summary>
    public static string StageCopy(string src, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".dlssu-tmp";
        try
        {
            ClearReadOnly(tmp);
            File.Copy(src, tmp, true);
        }
        catch
        {
            TryDeleteFile(tmp);
            throw;
        }
        return tmp;
    }

    /// <summary>
    /// Moves a staged temp file over <paramref name="dest"/> in one rename. A read-only target (users lock configs and
    /// dlls that way) is replaced and stays read-only. On failure the temp file goes and <paramref name="dest"/> is untouched.
    /// Once the rename is done the swap counts as done: putting the lock back is best effort.
    /// </summary>
    public static void CommitStaged(string tmp, string dest)
    {
        var locked = false;
        try
        {
            locked = ClearReadOnly(dest);
            File.Move(tmp, dest, true);
        }
        catch
        {
            TryDeleteFile(tmp);
            if (locked) TryLock(dest);
            throw;
        }
        if (locked) TryLock(dest);
    }

    private static void TryLock(string path)
    {
        try
        {
            if (File.Exists(path)) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"{path} could not be made read-only again ({ex.Message})");
        }
    }

    /// <summary>Writes via a temp file that is flushed to disk before the rename, so a power loss leaves the old or the new text, never a torn file.</summary>
    public static void AtomicWriteText(string dest, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".dlssu-tmp";
        try
        {
            ClearReadOnly(tmp);
            using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.Write(new UTF8Encoding(false).GetBytes(text));
            fs.Flush(flushToDisk: true);
        }
        catch
        {
            TryDeleteFile(tmp);
            throw;
        }
        CommitStaged(tmp, dest);
    }

    /// <summary>Takes the read-only flag off <paramref name="path"/>; true when it had one.</summary>
    public static bool ClearReadOnly(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || !info.IsReadOnly) return false;
        info.IsReadOnly = false;
        return true;
    }

    /// <summary>Deletes a file, read-only or not; a missing file is fine.</summary>
    public static void DeleteFile(string path)
    {
        if (!File.Exists(path)) return;
        ClearReadOnly(path);
        File.Delete(path);
    }

    /// <summary>
    /// Deletes a folder of ours with everything in it, read-only files included. Links inside are removed, never followed,
    /// so nothing outside the folder is touched.
    /// </summary>
    public static void DeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        // A link in place of the folder: only the link goes, never what it points to.
        if (IsLink(dir))
        {
            Directory.Delete(dir);
            return;
        }
        var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        foreach (var f in Directory.EnumerateFiles(dir, "*", walk)) ClearReadOnly(f);
        Directory.Delete(dir, true);
    }

    /// <summary>Best-effort cleanup of a file of ours (a temp file) after another error, which is the one reported.</summary>
    public static void TryDeleteFile(string path)
    {
        try { DeleteFile(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static bool IsUnder(string path, string root)
    {
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Full path without a trailing separator, except for a drive root: "C:" alone means the current folder on C.</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetPathRoot(full) ?? "";
        return trimmed.Length < root.Length ? root : trimmed;
    }

    /// <summary>
    /// The first junction or symbolic link on the way from <paramref name="root"/> (not included) down to
    /// <paramref name="path"/> (included), or null. Other reparse points (OneDrive placeholders, dedup) are not links.
    /// </summary>
    public static string? LinkBelow(string root, string path)
    {
        var r = Normalize(root);
        var cur = Normalize(path);
        while (cur.Length > r.Length && IsUnder(cur, r))
        {
            if (IsLink(cur)) return cur;
            cur = Path.GetDirectoryName(cur)!;
        }
        return null;
    }

    private static bool IsLink(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) == 0) return false;
            FileSystemInfo info = (attrs & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
            return info.LinkTarget is not null;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public static void TryDeleteEmptyDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
