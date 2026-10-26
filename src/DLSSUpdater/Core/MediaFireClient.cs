using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DLSSUpdater.Core;

public sealed class MediaFireCacheEntry
{
    public string Body { get; set; } = "";
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
}

internal sealed record MediaFireFile(string Name, string Hash, DateTimeOffset Created, string Landing, string? Version, int Rank);
internal sealed record MediaFireListing(IReadOnlyList<MediaFireFile> Files, bool More);
internal sealed record LecramChannel(string Key, MediaFireFile? Latest, bool Offline, string? Notice);

/// <summary>The public update channel used by DLSS5-NR-Setup; requests within a check are sequential.</summary>
internal sealed partial class MediaFireClient
{
    /// <summary>
    /// The author's update folders, newest generation first. Each major gets its own folder and no older folder points to it
    /// (v12's installer: "v12's own folder, so v11 setups never see v12"), so a new major is added here with a release.
    /// </summary>
    internal static readonly string[] Folders = ["r1xp6x0i043wx" /* v12 */, "wrwi1qkpgy0qu" /* v11 */];
    internal static readonly string DefaultFolder = Folders[0];
    private const int MaxDownload = 256 * 1024 * 1024;
    private static readonly HttpClient SharedHttp = CreateHttp(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(15),
    });
    private readonly HttpClient _http;

    internal MediaFireClient() => _http = SharedHttp;
    internal MediaFireClient(HttpMessageHandler handler) => _http = CreateHttp(handler);

    private static HttpClient CreateHttp(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DLSS5Updater/1.0");
        return http;
    }

    [GeneratedRegex(@"^v(\d+)\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();

    internal static string? VersionOf(string name)
    {
        var prefix = name.StartsWith("DLSS5-NR-Setup-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? "DLSS5-NR-Setup-" : name.StartsWith("DLSS5-NR-", StringComparison.OrdinalIgnoreCase)
                && (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)) ? "DLSS5-NR-" : null;
        if (prefix is null) return null;
        var version = name[prefix.Length..^Path.GetExtension(name).Length];
        var match = VersionPattern().Match(version);
        return match.Success && int.TryParse(match.Groups[1].Value, out var major) && major >= 11 ? version : null;
    }

    internal static MediaFireListing ParseListing(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("response").GetProperty("folder_content");
        var files = new List<MediaFireFile>();
        foreach (var item in content.GetProperty("files").EnumerateArray())
        {
            static string Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            var name = Text(item, "filename");
            var hash = Text(item, "hash");
            var created = Text(item, "created_utc");
            var landing = item.TryGetProperty("links", out var links) ? Text(links, "normal_download") : "";
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || !Https(landing)) continue;
            if (!DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) continue;
            var version = VersionOf(name);
            var raw = name.Equals(ComponentStore.Lecram11File, StringComparison.OrdinalIgnoreCase);
            if (!raw && version is null && !name.Equals("channel.json", StringComparison.OrdinalIgnoreCase)) continue;
            files.Add(new MediaFireFile(name, hash, date, landing, version,
                name.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase) ? 2 : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 1 : 0));
        }
        return new MediaFireListing(files, content.TryGetProperty("more_chunks", out var more) && string.Equals(more.GetString(), "yes", StringComparison.OrdinalIgnoreCase));
    }

    internal static MediaFireFile? SelectLatest(IEnumerable<MediaFireFile> files) => files
        .Where(f => f.Version is not null || f.Name.Equals(ComponentStore.Lecram11File, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(f => f.Created).ThenByDescending(f => f.Rank).FirstOrDefault();

    /// <summary>
    /// Checks the folders newest generation first and takes the first one with a build, so a late fix in an older generation's
    /// folder never wins over a newer major. A key the settings remembered from a move (not one of <see cref="Folders"/>) goes first.
    /// </summary>
    internal async Task<LecramChannel> CheckFoldersAsync(string remembered, CancellationToken ct)
    {
        var keys = Folders.Contains(remembered, StringComparer.OrdinalIgnoreCase) ? Folders : [remembered, .. Folders];
        LecramChannel? first = null;
        foreach (var key in keys)
        {
            var channel = await CheckAsync(key, ct);
            if (channel.Offline || channel.Latest is not null) return channel;
            first ??= channel;
        }
        return first!;
    }

    internal async Task<LecramChannel> CheckAsync(string key, CancellationToken ct)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? notice = null;
        for (var hop = 0; hop < 2; hop++)
        {
            if (!ValidKey(key) || !visited.Add(key)) throw new InvalidDataException("Invalid or repeated MediaFire channel key.");
            var files = new List<MediaFireFile>();
            var offline = false;
            for (var chunk = 1; chunk <= 10; chunk++)
            {
                var (listing, cached) = await ListingAsync(key, chunk, ct);
                files.AddRange(listing.Files);
                offline |= cached;
                if (!listing.More) break;
            }
            var channel = files.FirstOrDefault(f => f.Name.Equals("channel.json", StringComparison.OrdinalIgnoreCase));
            if (offline || channel is null) return new LecramChannel(key, SelectLatest(files), offline, notice);
            var temp = Path.Combine(AppPaths.Cache, "lecram-channel-" + Guid.NewGuid().ToString("N"));
            try
            {
                await DownloadAsync(channel, temp, ct);
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(temp, ct));
                if (doc.RootElement.TryGetProperty("notice", out var n)) notice = n.GetString();
                var moved = doc.RootElement.TryGetProperty("moved_to", out var m) ? m.GetString() : null;
                if (string.IsNullOrWhiteSpace(moved)) return new LecramChannel(key, SelectLatest(files), false, notice);
                if (hop == 1 || visited.Contains(moved) || !ValidKey(moved)) throw new InvalidDataException("MediaFire channel migration loop or multiple moves.");
                key = moved;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        throw new InvalidDataException("MediaFire channel could not be resolved.");
    }

    private static bool ValidKey(string key) => key.Length is > 0 and <= 64 && key.All(char.IsAsciiLetterOrDigit);
    private static bool Https(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private async Task<(MediaFireListing Listing, bool Offline)> ListingAsync(string key, int chunk, CancellationToken ct)
    {
        var file = Path.Combine(AppPaths.Cache, $"lecram-mediafire-{key}-{chunk}.json");
        MediaFireCacheEntry? cache = null;
        try
        {
            if (File.Exists(file)) cache = JsonSerializer.Deserialize(File.ReadAllText(file), JsonCtx.Default.MediaFireCacheEntry);
        }
        catch (Exception ex) when (ex is IOException or JsonException) { /* A damaged cache can be replaced by a fresh listing. */ }
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://www.mediafire.com/api/1.5/folder/get_content.php?folder_key={key}&content_type=files&chunk={chunk}&response_format=json");
        if (EntityTagHeaderValue.TryParse(cache?.ETag, out var etag)) request.Headers.IfNoneMatch.Add(etag);
        request.Headers.IfModifiedSince = cache?.LastModified;
        try
        {
            using var response = await _http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotModified && cache is not null) return (ParseListing(cache.Body), false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(ct);
            var listing = ParseListing(body);
            var entry = new MediaFireCacheEntry { Body = body, ETag = response.Headers.ETag?.ToString(), LastModified = response.Content.Headers.LastModified };
            FileUtil.AtomicWriteText(file, JsonSerializer.Serialize(entry, JsonCtx.Default.MediaFireCacheEntry));
            return (listing, false);
        }
        catch (Exception ex) when (cache is not null && !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
        { return (ParseListing(cache.Body), true); }
    }

    [GeneratedRegex("href\\s*=\\s*\"(https://download[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadLinkPattern();

    internal static string DownloadLink(string html)
    {
        var match = DownloadLinkPattern().Match(html);
        var url = WebUtility.HtmlDecode(match.Groups[1].Value);
        return match.Success && Https(url) ? url : throw new InvalidDataException("MediaFire download link was not found.");
    }

    internal async Task DownloadAsync(MediaFireFile file, string dest, CancellationToken ct)
    {
        var html = await _http.GetStringAsync(file.Landing, ct);
        using var response = await _http.GetAsync(DownloadLink(html), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxDownload) throw new InvalidDataException("Lecram package is too large.");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(dest))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    if (output.Length + read > MaxDownload) throw new InvalidDataException("Lecram package is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            if (!FileUtil.Sha256(dest).Equals(file.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MediaFire SHA-256 mismatch; download refused.");
        }
        catch
        {
            if (File.Exists(dest)) File.Delete(dest);
            throw;
        }
    }

    /// <summary>Uses the EOCD to locate a ZIP with either file-relative or ZIP-relative offsets; never runs the PE.</summary>
    internal static void ExtractAddon(string package, string dest)
    {
        var data = File.ReadAllBytes(package);
        var end = -1;
        for (var i = data.Length - 22; i >= Math.Max(0, data.Length - 65557); i--)
        {
            if (BitConverter.ToUInt32(data, i) == 0x06054b50 && i + 22 + BitConverter.ToUInt16(data, i + 20) == data.Length)
            { end = i; break; }
        }
        if (end < 0) throw new InvalidDataException("Lecram package has no appended ZIP.");
        var start = (long)end - BitConverter.ToUInt32(data, end + 12) - BitConverter.ToUInt32(data, end + 16);
        if (start < 0 || start > end) throw new InvalidDataException("Invalid Lecram ZIP offsets.");
        using var stream = new MemoryStream(data, (int)start, data.Length - (int)start, false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = zip.Entries.Where(e => e.FullName.Equals(ComponentStore.Lecram11File, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length != 1 || entries[0].Length > 128 * 1024 * 1024) throw new InvalidDataException("Lecram ZIP must contain one root dlss5-nr.addon64.");
        entries[0].ExtractToFile(dest);
    }
}
