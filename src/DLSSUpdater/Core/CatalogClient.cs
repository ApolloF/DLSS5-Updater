using System.Text.Json;

namespace DLSSUpdater.Core;

public enum CatalogOrigin
{
    /// <summary>GitHub answered (200, or 304 from the ETag cache); the copy under AppPaths was refreshed.</summary>
    Online,
    /// <summary>GitHub couldn't be reached or answered with something unusable; the last good copy is used.</summary>
    Cached,
    /// <summary>Nothing online and no earlier copy: the catalog features stay off.</summary>
    None,
}

/// <param name="Note">One line for the log or the UI when the result isn't a plain online refresh.</param>
public sealed record CatalogLoad(CatalogDocument? Document, CatalogOrigin Origin, string? Note);

/// <summary>
/// Fetches catalog.json from the catalog repository through <see cref="GitHubClient"/> (ETag cache) and keeps the last good
/// copy under <see cref="AppPaths.CatalogFile"/>. Never throws at the caller: offline, rate-limited, damaged or newer-than-us
/// answers all fall back to the last good copy.
/// </summary>
public sealed class CatalogClient(GitHubClient github, string? repo = null)
{
    public const string DefaultRepo = "ApolloF/DLSS5-Updater-catalog";

    /// <summary>Highest schema version this build understands.</summary>
    public const int SupportedSchema = 1;

    private readonly string _repo = repo ?? DefaultRepo;

    public async Task<CatalogLoad> LoadAsync(CancellationToken ct)
    {
        string? problem;
        try
        {
            var (body, stale) = await github.GetRepoFileAsync(_repo, "catalog.json", ct);
            if (!TryParse(body, out var doc, out problem))
            {
                Log.Warn($"Catalog from {_repo} unusable ({problem}); using the last good copy");
                return FromCache($"online catalog unusable: {problem}");
            }
            if (stale) return new CatalogLoad(doc, CatalogOrigin.Cached, "GitHub unreachable");
            Save(body);
            return new CatalogLoad(doc, CatalogOrigin.Online, null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Info($"Catalog not reachable ({ex.Message}); using the last good copy");
            return FromCache("GitHub unreachable");
        }
    }

    /// <summary>The last good copy, without any request (startup, offline mode).</summary>
    public CatalogLoad LoadCached() => FromCache(null);

    private static CatalogLoad FromCache(string? note)
    {
        try
        {
            if (File.Exists(AppPaths.CatalogFile) && TryParse(File.ReadAllText(AppPaths.CatalogFile), out var doc, out _))
                return new CatalogLoad(doc, CatalogOrigin.Cached, note);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new CatalogLoad(null, CatalogOrigin.None, note is null ? "no catalog yet" : $"{note}, and no earlier copy");
    }

    private static void Save(string body)
    {
        try { FileUtil.AtomicWriteText(AppPaths.CatalogFile, body); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Catalog copy could not be saved ({ex.Message})");
        }
    }

    /// <summary>Parses and checks a catalog: valid JSON, a schema this build knows, and a renodx section to work with.</summary>
    internal static bool TryParse(string json, out CatalogDocument doc, out string? problem)
    {
        doc = new CatalogDocument();
        try
        {
            var parsed = JsonSerializer.Deserialize(json, JsonCtx.Default.CatalogDocument);
            if (parsed is null) { problem = "empty"; return false; }
            if (parsed.Schema < 1) { problem = "no schema version"; return false; }
            if (parsed.Schema > SupportedSchema) { problem = $"schema {parsed.Schema} is newer than this app understands ({SupportedSchema}); update the app"; return false; }
            doc = parsed;
            problem = null;
            return true;
        }
        catch (JsonException ex)
        {
            problem = $"not valid JSON ({ex.Message})";
            return false;
        }
    }
}
