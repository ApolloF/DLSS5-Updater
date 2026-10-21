using System.Security.Cryptography;

namespace DLSSUpdater.Core;

/// <param name="Path">The verified file, or null when it was refused.</param>
/// <param name="Refusal">One line saying why a file was refused, for the log and the UI.</param>
/// <param name="FromMirror">True when the bytes came from the catalog's mirror rather than the author's own URL.</param>
public sealed record CatalogDownload(string? Path, string? Refusal, bool FromMirror)
{
    public bool Ok => Path is not null;
}

/// <summary>
/// Downloads a catalog file (mirror first, then the author's URL) into a staging file, checks size and SHA-256 against
/// the catalog, and only then moves it to its destination. A mismatch leaves nothing behind and returns a one-line reason.
/// </summary>
public sealed class CatalogDownloader(GitHubClient github)
{
    public async Task<CatalogDownload> DownloadAsync(CatalogBinary file, string dest, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!file.IsInstallable)
            return new CatalogDownload(null, $"{file.Name}: refused, the catalog has no SHA-256 for it", false);

        var sources = new List<(string Url, bool Mirror)>();
        if (!string.IsNullOrWhiteSpace(file.MirrorUrl)) sources.Add((file.MirrorUrl, true));
        if (!string.IsNullOrWhiteSpace(file.Url) && file.Url != file.MirrorUrl) sources.Add((file.Url, false));

        string? last = null;
        foreach (var (url, mirror) in sources)
        {
            var staging = dest + ".catalog-part";
            try
            {
                await github.DownloadAsync(url, staging, file.Name, progress, ct);
                last = Check(file, staging, mirror);
                if (last is null)
                {
                    File.Move(staging, dest, true);
                    return new CatalogDownload(dest, null, mirror);
                }
                Log.Warn(last);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or IOException or TaskCanceledException)
            {
                last = $"{file.Name}: download from {(mirror ? "the mirror" : "the author")} failed ({ex.Message})";
                Log.Info(last);
            }
            finally
            {
                FileUtil.TryDeleteFile(staging);
                FileUtil.TryDeleteFile(staging + ".part");
            }
        }
        return new CatalogDownload(null, last ?? $"{file.Name}: refused, the catalog lists no download address", false);
    }

    /// <summary>Null when the file is what the catalog says, else the one-line refusal.</summary>
    private static string? Check(CatalogBinary file, string path, bool mirror)
    {
        var where = mirror ? "mirror copy" : "download";
        var size = new FileInfo(path).Length;
        if (file.Size > 0 && size != file.Size)
            return $"{file.Name}: refused, the {where} is {size} bytes but the catalog says {file.Size}";

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        var actual = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        if (!actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            return mirror
                ? $"{file.Name}: refused, the mirror copy doesn't match the catalog's SHA-256"
                : $"{file.Name}: refused, the author's file changed since the catalog was built (SHA-256 {actual[..12]}… instead of {file.Sha256![..12]}…); try again after the next catalog update";
        return null;
    }
}
