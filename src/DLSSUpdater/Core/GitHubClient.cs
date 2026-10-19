using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

public sealed class GhRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("published_at")] public DateTime? PublishedAt { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("assets")] public List<GhAsset> Assets { get; set; } = [];
    /// <summary>Served from the ETag cache because GitHub answered with an error or couldn't be reached.</summary>
    [JsonIgnore] public bool Stale { get; set; }
}

public sealed class GhAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("browser_download_url")] public string Url { get; set; } = "";
    [JsonPropertyName("digest")] public string? Digest { get; set; }

    public string? Sha256 => Digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
        ? d[7..].ToLowerInvariant()
        : null;
}

public sealed class ApiCacheEntry
{
    public string Url { get; set; } = "";
    public string ETag { get; set; } = "";
    public string Body { get; set; } = "";
}

public readonly record struct TransferProgress(string Text, double? Fraction);

/// <summary>Minimal GitHub REST client with ETag caching (304s don't count against the rate limit).</summary>
public sealed class GitHubClient
{
    private static readonly HttpClient SharedHttp = CreateHttp(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(15),
    });
    private readonly HttpClient _http;

    public GitHubClient() => _http = SharedHttp;

    /// <summary>For tests: every request goes to <paramref name="handler"/>.</summary>
    internal GitHubClient(HttpMessageHandler handler) => _http = CreateHttp(handler);

    public string? Token { get; set; }

    private static HttpClient CreateHttp(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DLSS5Updater", "1.0"));
        return http;
    }

    public async Task<List<GhRelease>> GetReleasesAsync(string repo, int perPage, CancellationToken ct)
    {
        var (body, stale) = await GetApiAsync($"https://api.github.com/repos/{repo}/releases?per_page={perPage}", ct);
        var releases = JsonSerializer.Deserialize(body, JsonCtx.Default.ListGhRelease) ?? [];
        foreach (var r in releases) r.Stale = stale;
        return releases;
    }

    /// <summary>Releases of <paramref name="repo"/> from the ETag cache only (no request), for when GitHub can't be reached.</summary>
    public List<GhRelease> CachedReleases(string repo)
    {
        var prefix = $"https://api.github.com/repos/{repo}/releases?";
        var releases = new List<GhRelease>();
        if (!Directory.Exists(AppPaths.ApiCacheDir)) return releases;
        foreach (var file in Directory.EnumerateFiles(AppPaths.ApiCacheDir, RepoPrefix(repo) + "*.json"))
        {
            if (ReadEntry(file) is not { } e || !e.Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            try { releases.AddRange(JsonSerializer.Deserialize(e.Body, JsonCtx.Default.ListGhRelease) ?? []); }
            catch (JsonException) { }
        }
        return releases;
    }

    public async Task<List<string>> GetTagsAsync(string repo, int perPage, CancellationToken ct)
    {
        var (body, _) = await GetApiAsync($"https://api.github.com/repos/{repo}/tags?per_page={perPage}", ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.EnumerateArray()
            .Select(t => t.TryGetProperty("name", out var n) ? n.GetString() : null)
            .OfType<string>()
            .ToList();
    }

    /// <summary>Plain GET of a web page (not the GitHub API), 30 s timeout.</summary>
    public async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var res = await _http.GetAsync(url, cts.Token);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(cts.Token);
    }

    /// <summary>The token GitHub refused (401, or a 403 that isn't a rate limit); requests go without it until it changes.</summary>
    private string? _rejectedToken;

    /// <summary>
    /// Body of an API request, and whether it came from the ETag cache because GitHub failed (stale). A token GitHub
    /// refuses is dropped for the rest of the session, with one warning, so an expired token never hides new releases.
    /// </summary>
    private async Task<(string Body, bool Stale)> GetApiAsync(string url, CancellationToken ct)
    {
        var cacheFile = CacheFileFor(url);
        var cached = ReadEntry(cacheFile) is { } e && e.Url == url ? e : null;
        var token = string.IsNullOrWhiteSpace(Token) ? null : Token.Trim();
        if (token is not null && token == _rejectedToken) token = null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var res = await SendApiAsync(url, token, cached, cts.Token);
            try
            {
                if (token is not null && await IsTokenRejectedAsync(res, cts.Token))
                {
                    if (Interlocked.Exchange(ref _rejectedToken, token) != token)
                        Log.Warn($"GitHub rejected the saved access token ({(int)res.StatusCode}); it may be expired or revoked. " +
                                 "Checking without it for now. Replace or clear it in Settings.");
                    res.Dispose();
                    res = await SendApiAsync(url, null, cached, cts.Token);
                }
                if (res.StatusCode == HttpStatusCode.NotModified && cached is not null) return (cached.Body, false);
                if (!res.IsSuccessStatusCode && cached is not null)
                {
                    Log.Info($"GitHub returned {(int)res.StatusCode}, using cached release data");
                    return (cached.Body, true);
                }
                res.EnsureSuccessStatusCode();
                var body = await res.Content.ReadAsStringAsync(cts.Token);
                if (res.Headers.ETag is { } etag)
                    WriteEntry(cacheFile, new ApiCacheEntry { Url = url, ETag = etag.ToString(), Body = body });
                return (body, false);
            }
            finally { res.Dispose(); }
        }
        catch (Exception ex) when (cached is not null && !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
        {
            Log.Info($"GitHub unreachable ({ex.Message}), using cached release data");
            return (cached.Body, true);
        }
    }

    private async Task<HttpResponseMessage> SendApiAsync(string url, string? token, ApiCacheEntry? cached, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (cached is not null) req.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(cached.ETag));
        return await _http.SendAsync(req, ct);
    }

    /// <summary>
    /// 401, or a 403 that isn't a rate limit. GitHub's rate-limit 403s carry no requests left, a Retry-After, or (the
    /// secondary limit, which may have neither) a message about the rate limit or abuse detection.
    /// </summary>
    internal static async Task<bool> IsTokenRejectedAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.StatusCode == HttpStatusCode.Unauthorized) return true;
        if (res.StatusCode != HttpStatusCode.Forbidden) return false;
        if (res.Headers.RetryAfter is not null) return false;
        if (res.Headers.TryGetValues("X-RateLimit-Remaining", out var left) && left.FirstOrDefault() == "0") return false;
        var body = await res.Content.ReadAsStringAsync(ct);
        return !body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
               && !body.Contains("abuse", StringComparison.OrdinalIgnoreCase);
    }

    public async Task DownloadAsync(string url, string dest, string label, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".part";
        using (var res = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
            var total = res.Content.Headers.ContentLength;
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
            var buffer = new byte[1 << 20];
            long done = 0;
            var lastReport = Environment.TickCount64;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (progress is not null && Environment.TickCount64 - lastReport > 100)
                {
                    lastReport = Environment.TickCount64;
                    progress.Report(new TransferProgress(
                        $"Downloading {label}  {done / 1048576.0:0.0} / {(total ?? 0) / 1048576.0:0.0} MB",
                        total > 0 ? (double)done / total.Value : null));
                }
            }
        }
        File.Move(tmp, dest, true);
    }

    /// <summary>True when the file is there, false only on a 404; anything else (rate limit, server error) throws.</summary>
    public async Task<bool> ExistsAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Head, url);
        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return false;
        res.EnsureSuccessStatusCode();
        return true;
    }

    // One small file per request, read only when that request runs. A single file for everything grew to tens of MB
    // (release lists with hundreds of assets) and was loaded and rewritten whole, holding ~1 GB of buffers.

    private static readonly object CacheGate = new();

    /// <summary>"owner~repo~" for repo URLs, so the cached releases of one repo are found by file name.</summary>
    private static string RepoPrefix(string repo) => repo.Replace('/', '~') + "~";

    internal static string CacheFileFor(string url)
    {
        const string repos = "https://api.github.com/repos/";
        var prefix = "";
        if (url.StartsWith(repos, StringComparison.OrdinalIgnoreCase) && url[repos.Length..].Split('/') is { Length: >= 3 } p)
            prefix = RepoPrefix($"{p[0]}/{p[1]}");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))[..16];
        var name = string.Concat((prefix + hash).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(AppPaths.ApiCacheDir, name.ToLowerInvariant() + ".json");
    }

    private static ApiCacheEntry? ReadEntry(string file)
    {
        DropLegacyCache();
        try
        {
            if (!File.Exists(file)) return null;
            using var fs = File.OpenRead(file);
            return JsonSerializer.Deserialize(fs, JsonCtx.Default.ApiCacheEntry);
        }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }

    private static void WriteEntry(string file, ApiCacheEntry entry)
    {
        try
        {
            lock (CacheGate) FileUtil.AtomicWriteText(file, JsonSerializer.Serialize(entry, JsonCtx.Default.ApiCacheEntry));
        }
        catch (IOException) { }
    }

    private static bool _legacyChecked;

    /// <summary>The old single-file cache costs one extra request per repo to drop, and nothing reads it anymore.</summary>
    private static void DropLegacyCache()
    {
        if (_legacyChecked) return;
        _legacyChecked = true;
        try { File.Delete(AppPaths.LegacyApiCacheFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
