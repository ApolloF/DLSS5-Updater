using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DLSSUpdater.Core;

/// <summary>How this copy got onto the PC: the Inno Setup installer, or the portable exe run from anywhere.</summary>
public enum AppInstallKind { Portable, Installed }

/// <summary>A newer stable release of DLSS5 Updater itself.</summary>
public sealed record AppRelease(Version Version, string Tag, string? Notes, DateTime? Published, GhRelease Release)
{
    public string Label => "v" + Version.ToString(3);
}

/// <summary>An update was refused (checksum missing or wrong, asset missing); the message is meant for the user.</summary>
public sealed class AppUpdateException(string message) : InvalidOperationException(message);

/// <summary>
/// Self-update rules without UI: which release is newer, which asset fits this copy, checksum checks, the throttle and
/// the portable exe swap. <see cref="AppUpdater"/> does the network and process work on top.
/// </summary>
public static partial class AppUpdate
{
    public const string Repo = "ApolloF/DLSS5-Updater";
    /// <summary>Earlier names of <see cref="Repo"/>: GitHub redirects them, and asset URLs may still carry one.</summary>
    private static readonly string[] RepoNames = [Repo, "ApolloF/dlss5updater"];
    public const string PortableAsset = "DLSS5Updater.exe";
    public const string SumsAsset = "SHA256SUMS";
    /// <summary>AppId in installer/DLSSUpdater.iss; Inno Setup keeps the uninstall entry under "&lt;AppId&gt;_is1".</summary>
    public const string InnoAppId = "{6F0B8E53-1C2D-4B7A-9E4F-3A5D2C8B1E07}";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    /// <summary>A checksum file is a few lines; anything bigger isn't one.</summary>
    private const long MaxSumsSize = 64 * 1024;

    public static Version Current => Normalize(typeof(AppUpdate).Assembly.GetName().Version) ?? new Version(0, 0, 0);

    public static string SetupAsset(Version v) => $"DLSS5Updater-Setup-{v.ToString(3)}.exe";

    /// <summary>Due when never checked, the interval has passed, or the clock went backwards since.</summary>
    public static bool IsDue(DateTime? lastCheckUtc, DateTime nowUtc) =>
        lastCheckUtc is not { } last || last > nowUtc || nowUtc - last >= CheckInterval;

    /// <summary>The version of a stable release ("v1.12.0" → 1.12.0); null for drafts, pre-releases and tags like "v2.1.0-beta".</summary>
    public static Version? StableVersion(GhRelease r)
    {
        if (r.Draft || r.Prerelease) return null;
        return StableTag().IsMatch(r.TagName.Trim()) ? ParseVersion(r.TagName) : null;
    }

    /// <summary>The newest stable release above <paramref name="current"/>, or null when this copy is up to date.</summary>
    public static AppRelease? Newest(IEnumerable<GhRelease> releases, Version current) =>
        releases.Select(r => (Release: r, Version: StableVersion(r)))
            .Where(x => x.Version is not null && x.Version > current)
            .OrderByDescending(x => x.Version)
            .Select(x => new AppRelease(x.Version!, x.Release.TagName, x.Release.Body, x.Release.PublishedAt, x.Release))
            .FirstOrDefault();

    /// <summary>The download for this kind of copy, only when it is the release's own asset on github.com over HTTPS.</summary>
    public static GhAsset? PackageAsset(AppRelease r, AppInstallKind kind) =>
        OwnAsset(r, kind == AppInstallKind.Installed ? SetupAsset(r.Version) : PortableAsset);

    public static GhAsset? SumsFile(AppRelease r) => OwnAsset(r, SumsAsset) is { Size: <= MaxSumsSize } a ? a : null;

    private static GhAsset? OwnAsset(AppRelease r, string name) =>
        r.Release.Assets.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && IsOwnAssetUrl(a.Url, r.Tag, a.Name));

    internal static bool IsOwnAssetUrl(string url, string tag, string name) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && RepoNames.Any(repo => url.Equals($"https://github.com/{repo}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}", StringComparison.OrdinalIgnoreCase));

    /// <summary>"&lt;sha256&gt;  &lt;file&gt;" lines (sha256sum format, text or binary marker) → file name to lower-case hash.</summary>
    public static Dictionary<string, string> ParseSums(string text)
    {
        var sums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            if (SumLine().Match(line.Trim()) is { Success: true } m)
                sums.TryAdd(Path.GetFileName(m.Groups[2].Value.Trim()), m.Groups[1].Value.ToLowerInvariant());
        }
        return sums;
    }

    /// <summary>
    /// Null when <paramref name="sha256"/> is what SHA256SUMS lists for <paramref name="asset"/>, and matches GitHub's
    /// own digest and size when the API has them; otherwise why the file is refused.
    /// </summary>
    public static string? Verify(GhAsset asset, string sha256, long length, IReadOnlyDictionary<string, string> sums)
    {
        if (!sums.TryGetValue(asset.Name, out var expected))
            return $"The release has no checksum for {asset.Name}, so it wasn't installed.";
        if (!expected.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            return $"{asset.Name} doesn't match its published checksum, so it wasn't installed.";
        if (asset.Sha256 is { } digest && !digest.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            return $"{asset.Name} doesn't match GitHub's checksum, so it wasn't installed.";
        if (asset.Size > 0 && asset.Size != length)
            return $"{asset.Name} has the wrong size, so it wasn't installed.";
        return null;
    }

    // ---------- install kind ----------

    /// <summary>Installed when Inno Setup's uninstall entry points at this exe's folder (or its uninstaller sits next to it).</summary>
    public static AppInstallKind DetectKind(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath)!;
        return IsInstalledAt(dir, InstallLocations().Select(l => l.Location)) || File.Exists(Path.Combine(dir, "unins000.exe"))
            ? AppInstallKind.Installed
            : AppInstallKind.Portable;
    }

    /// <summary>The installer entry for this folder is machine-wide, so a silent upgrade would ask for admin rights.</summary>
    public static bool NeedsAdmin(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath)!;
        return InstallLocations().Any(l => l.Machine && IsInstalledAt(dir, [l.Location]));
    }

    internal static bool IsInstalledAt(string exeDir, IEnumerable<string?> installLocations) =>
        installLocations.OfType<string>().Any(l => l.Length > 0 && SameDir(l, exeDir));

    private static bool SameDir(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static IEnumerable<(string? Location, bool Machine)> InstallLocations()
    {
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + InnoAppId + "_is1";
        foreach (var (hive, machine) in new[] { (RegistryHive.CurrentUser, false), (RegistryHive.LocalMachine, true) })
        {
            string? location = null;
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var k = root.OpenSubKey(key);
                location = k?.GetValue("InstallLocation") as string;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            if (location is not null) yield return (location, machine);
        }
    }

    // ---------- staged files ----------

    /// <summary>The portable download waits next to the running exe, so the swap is a rename on the same drive.</summary>
    public static string StagedPortable(string exePath) => exePath + ".new";

    public static string OldExe(string exePath) => exePath + ".old";

    public static string SetupDir => Path.Combine(AppPaths.Cache, "app-update");

    public static string StagedSetup(Version v) => Path.Combine(SetupDir, SetupAsset(v));

    public static string StagedPath(AppInstallKind kind, string exePath, Version v) =>
        kind == AppInstallKind.Installed ? StagedSetup(v) : StagedPortable(exePath);

    /// <summary>
    /// Puts <paramref name="staged"/> in place of the running <paramref name="exePath"/>: Windows lets a running exe be
    /// renamed, so it moves to *.old (deleted on the next start) and the new file takes its name. On failure the old exe is back.
    /// </summary>
    public static void SwapPortable(string exePath, string staged)
    {
        var old = OldExe(exePath);
        FileUtil.TryDeleteFile(old);
        File.Move(exePath, old);
        try { File.Move(staged, exePath); }
        catch
        {
            File.Move(old, exePath);
            throw;
        }
    }

    /// <summary>
    /// Start-up cleanup: the exe left behind by a portable swap, half-finished downloads, and staged files for a
    /// version that is no longer newer. <paramref name="keep"/> is a staged update that is still pending.
    /// </summary>
    public static void Cleanup(string exePath, Version? keep)
    {
        var current = Current;
        var keepIt = keep is not null && keep > current;
        FileUtil.TryDeleteFile(OldExe(exePath));
        FileUtil.TryDeleteFile(StagedPortable(exePath) + ".part");
        if (!keepIt) FileUtil.TryDeleteFile(StagedPortable(exePath));
        if (!Directory.Exists(SetupDir)) return;
        foreach (var f in Directory.EnumerateFiles(SetupDir))
            if (!keepIt || !Path.GetFileName(f).Equals(SetupAsset(keep!), StringComparison.OrdinalIgnoreCase)) FileUtil.TryDeleteFile(f);
    }

    /// <summary>Inno Setup switches for an unattended upgrade; setup restarts the app only with <paramref name="relaunch"/>.</summary>
    public static string SetupArgs(bool relaunch) =>
        "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS" + (relaunch ? " /RELAUNCH=1" : "");

    /// <summary>Release notes as plain text for a dialog: headings, bullets, emphasis and links reduced, length capped.</summary>
    public static string PlainNotes(string? markdown, int max = 4000)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "This release has no notes.";
        var sb = new StringBuilder();
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith('<')) continue;
            line = Heading().Replace(line, "");
            line = Bullet().Replace(line, "$1• ");
            line = Link().Replace(line, "$1");
            line = line.Replace("**", "").Replace("__", "").Replace("`", "");
            sb.Append(line).Append('\n');
        }
        var text = Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n").Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    /// <summary>"v2.1" or "2.1.0.0" → 2.1.0, so versions from tags and the assembly compare equal.</summary>
    public static Version? ParseVersion(string? tag) => Normalize(FileUtil.ParseTag(tag));

    private static Version? Normalize(Version? v) => v is null ? null : new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

    [GeneratedRegex(@"^[vV]?\d+(\.\d+){1,3}$")]
    private static partial Regex StableTag();

    [GeneratedRegex(@"^([0-9a-fA-F]{64})\s+\*?(.+)$")]
    private static partial Regex SumLine();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)[-*+]\s+")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();
}

/// <summary>What a check found: a newer release (or null) and the text for Settings. Never throws at the user.</summary>
public sealed record AppUpdateCheck(AppRelease? Update, string Message, bool Failed);

/// <summary>Network and process side of the self-update: checking GitHub, downloading and verifying, starting the swap or setup.</summary>
public sealed class AppUpdater(GitHubClient gh)
{
    public async Task<AppUpdateCheck> CheckAsync(Version current, CancellationToken ct)
    {
        try
        {
            var releases = await gh.GetReleasesAsync(AppUpdate.Repo, 10, ct);
            var stale = releases.Any(r => r.Stale);
            var update = AppUpdate.Newest(releases, current);
            var when = DateTime.Now.ToString("d MMM HH:mm", System.Globalization.CultureInfo.CurrentCulture);
            var found = update is null ? $"Up to date (v{current.ToString(3)})." : $"{update.Label} is available.";
            return new AppUpdateCheck(update, found + (stale ? " GitHub couldn't be reached; this is from the last check." : $" Checked {when}."), false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException)
        {
            Log.Info($"DLSS5 Updater update check skipped ({ex.Message})");
            return new AppUpdateCheck(null, "Couldn't reach GitHub. Trying again later.", true);
        }
    }

    /// <summary>
    /// Downloads the asset for <paramref name="kind"/> to its staging path and checks it against SHA256SUMS. Returns the
    /// staged file and its hash; a missing or wrong checksum deletes the file and throws <see cref="AppUpdateException"/>.
    /// </summary>
    public async Task<(string Path, string Sha256)> DownloadAsync(AppRelease r, AppInstallKind kind, string exePath,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var asset = AppUpdate.PackageAsset(r, kind)
                    ?? throw new AppUpdateException($"{r.Label} has no {(kind == AppInstallKind.Installed ? "installer" : "portable exe")} to download.");
        var sumsAsset = AppUpdate.SumsFile(r) ?? throw new AppUpdateException($"{r.Label} has no {AppUpdate.SumsAsset} file, so it wasn't installed.");
        var sums = AppUpdate.ParseSums(await gh.GetStringAsync(sumsAsset.Url, ct));
        if (!sums.ContainsKey(asset.Name)) throw new AppUpdateException($"The release has no checksum for {asset.Name}, so it wasn't installed.");

        var dest = AppUpdate.StagedPath(kind, exePath, r.Version);
        try
        {
            await gh.DownloadAsync(asset.Url, dest, $"DLSS5 Updater {r.Label}", progress, ct);
            var sha = await Task.Run(() => FileUtil.Sha256(dest), ct);
            if (AppUpdate.Verify(asset, sha, new FileInfo(dest).Length, sums) is { } error) throw new AppUpdateException(error);
            return (dest, sha);
        }
        catch (Exception ex)
        {
            FileUtil.TryDeleteFile(dest);
            FileUtil.TryDeleteFile(dest + ".part");
            if (ex is UnauthorizedAccessException)
                throw new AppUpdateException($"DLSS5 Updater can't write to {Path.GetDirectoryName(dest)}. Move it to a folder you own, or download {r.Label} from GitHub.");
            throw;
        }
    }

    /// <summary>
    /// Starts the update from a staged file whose hash is still <paramref name="sha256"/>, right before the process exits.
    /// <paramref name="releaseLock"/> runs just before setup starts: setup refuses to run while the instance mutex is held.
    /// Setup closes and (with relaunch) restarts the app; the portable swap starts the new exe itself. False when the
    /// file changed or vanished, or the start failed (logged).
    /// </summary>
    public static bool Apply(AppInstallKind kind, string exePath, string staged, string sha256, bool relaunch, Action? releaseLock = null)
    {
        try
        {
            if (!File.Exists(staged) || !FileUtil.Sha256(staged).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn("The downloaded DLSS5 Updater update changed or is gone; not installing it.");
                FileUtil.TryDeleteFile(staged);
                return false;
            }
            if (kind == AppInstallKind.Installed)
            {
                releaseLock?.Invoke();
                Process.Start(new ProcessStartInfo(staged, AppUpdate.SetupArgs(relaunch)) { UseShellExecute = true })?.Dispose();
            }
            else
            {
                AppUpdate.SwapPortable(exePath, staged);
                releaseLock?.Invoke();
                // --restarted waits for this process to let go of the instance lock.
                if (relaunch) Process.Start(new ProcessStartInfo(exePath, "--restarted") { UseShellExecute = true })?.Dispose();
            }
            Log.Info($"Installing the DLSS5 Updater update ({(kind == AppInstallKind.Installed ? "setup" : "portable exe")})");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            Log.Error("Could not start the DLSS5 Updater update", ex);
            return false;
        }
    }

    /// <summary>
    /// The verified update automatic mode downloaded earlier, started when the app closes or starts. Skipped when the
    /// option is off, nothing newer is staged, it was already tried once (a failed install doesn't loop), or setup would
    /// need admin rights (a surprise UAC prompt). True when the update was started.
    /// </summary>
    public static bool ApplyPending(AppSettings s, string exePath, bool relaunch, Action? releaseLock = null)
    {
        if (!s.AutoInstallAppUpdates || s.AppUpdateStaged is not { } tag || s.AppUpdateStagedSha256 is not { } sha) return false;
        if (AppUpdate.ParseVersion(tag) is not { } v || v <= AppUpdate.Current) return false;
        if (s.AppUpdateTried == tag) return false;
        var kind = AppUpdate.DetectKind(exePath);
        if (kind == AppInstallKind.Installed && AppUpdate.NeedsAdmin(exePath)) return false;
        s.AppUpdateTried = tag;
        s.Save();
        return Apply(kind, exePath, AppUpdate.StagedPath(kind, exePath, v), sha, relaunch, releaseLock);
    }
}
