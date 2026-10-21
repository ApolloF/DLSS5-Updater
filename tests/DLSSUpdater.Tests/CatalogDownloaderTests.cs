using System.Net;
using System.Text;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Catalog downloads are verified against the catalog's SHA-256 and size before they reach a library. Fake handler, no network.</summary>
public sealed class CatalogDownloaderTests : IDisposable
{
    private const string Mirror = "https://mirror.test/abc-renodx-x.addon64";
    private const string Upstream = "https://author.test/renodx-x.addon64";
    private const string Good = "MZ the real build";

    private sealed class FakeWeb(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request.RequestUri!.ToString()));
        }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dlssu-catdl-" + Guid.NewGuid().ToString("N")[..8]);

    public CatalogDownloaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Dest => Path.Combine(_dir, "library", "renodx-x.addon64");

    private static CatalogBinary File(string content = Good, string? mirror = Mirror) => new()
    {
        Name = "renodx-x.addon64",
        Url = Upstream,
        MirrorUrl = mirror,
        Sha256 = CatalogFixtures.Sha(content),
        Size = Encoding.ASCII.GetByteCount(content),
    };

    private static HttpResponseMessage Body(string text) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(text)) };

    private static CatalogDownloader Downloader(Func<string, HttpResponseMessage> respond, out FakeWeb web)
    {
        web = new FakeWeb(respond);
        return new CatalogDownloader(new GitHubClient(web));
    }

    [Fact]
    public async Task A_matching_mirror_copy_lands_at_the_destination_and_upstream_is_never_asked()
    {
        var dl = Downloader(_ => Body(Good), out var web);

        var result = await dl.DownloadAsync(File(), Dest, null, default);

        Assert.True(result.Ok);
        Assert.True(result.FromMirror);
        Assert.Equal(Good, System.IO.File.ReadAllText(Dest));
        Assert.Equal([Mirror], web.Urls);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Dest)!, "*.catalog-part*"));
    }

    [Fact]
    public async Task A_pinned_file_downloads_from_the_authors_url()
    {
        var dl = Downloader(_ => Body(Good), out var web);

        var result = await dl.DownloadAsync(File(mirror: null), Dest, null, default);

        Assert.True(result.Ok);
        Assert.False(result.FromMirror);
        Assert.Equal([Upstream], web.Urls);
    }

    [Fact]
    public async Task A_hash_mismatch_is_refused_with_one_line_and_nothing_lands()
    {
        var dl = Downloader(_ => Body("MZ tampered content!"), out _);
        var file = File(mirror: null);
        file.Size = Encoding.ASCII.GetByteCount("MZ tampered content!");

        var result = await dl.DownloadAsync(file, Dest, null, default);

        Assert.False(result.Ok);
        Assert.Null(result.Path);
        Assert.Contains("refused", result.Refusal);
        Assert.Contains("changed since the catalog was built", result.Refusal);
        Assert.DoesNotContain('\n', result.Refusal!);
        Assert.False(System.IO.File.Exists(Dest));
        Assert.Empty(Directory.GetFiles(_dir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_wrong_size_is_refused_before_hashing()
    {
        var dl = Downloader(_ => Body(Good + "extra"), out _);

        var result = await dl.DownloadAsync(File(mirror: null), Dest, null, default);

        Assert.False(result.Ok);
        Assert.Contains("bytes but the catalog says", result.Refusal);
        Assert.False(System.IO.File.Exists(Dest));
    }

    [Fact]
    public async Task A_bad_mirror_copy_falls_back_to_the_authors_file_when_that_matches()
    {
        var dl = Downloader(url => url == Mirror ? Body("MZ corrupted mirror!") : Body(Good), out var web);
        var file = File();

        var result = await dl.DownloadAsync(file, Dest, null, default);

        Assert.True(result.Ok);
        Assert.False(result.FromMirror);
        Assert.Equal([Mirror, Upstream], web.Urls);
        Assert.Equal(Good, System.IO.File.ReadAllText(Dest));
    }

    [Fact]
    public async Task When_both_sources_are_wrong_the_last_reason_is_given_and_nothing_lands()
    {
        var dl = Downloader(_ => Body("MZ wrong wrong wrong!"), out _);
        var file = File();
        file.Size = 0;

        var result = await dl.DownloadAsync(file, Dest, null, default);

        Assert.False(result.Ok);
        Assert.Contains("author", result.Refusal);
        Assert.False(System.IO.File.Exists(Dest));
    }

    [Fact]
    public async Task An_unreachable_mirror_falls_back_to_upstream()
    {
        var dl = Downloader(url => url == Mirror ? new HttpResponseMessage(HttpStatusCode.NotFound) : Body(Good), out _);

        var result = await dl.DownloadAsync(File(), Dest, null, default);

        Assert.True(result.Ok);
        Assert.False(result.FromMirror);
    }

    [Fact]
    public async Task A_dead_download_is_a_refusal_not_an_exception()
    {
        var dl = Downloader(_ => throw new HttpRequestException("connection reset"), out _);

        var result = await dl.DownloadAsync(File(mirror: null), Dest, null, default);

        Assert.False(result.Ok);
        Assert.Contains("failed", result.Refusal);
        Assert.Contains("connection reset", result.Refusal);
    }

    [Fact]
    public async Task An_entry_without_a_hash_is_refused_without_any_request()
    {
        var dl = Downloader(_ => Body(Good), out var web);
        var file = File();
        file.Sha256 = null;

        var result = await dl.DownloadAsync(file, Dest, null, default);

        Assert.False(result.Ok);
        Assert.Contains("no SHA-256", result.Refusal);
        Assert.Empty(web.Urls);
    }

    [Fact]
    public async Task An_existing_file_is_replaced_only_by_a_verified_one()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Dest)!);
        System.IO.File.WriteAllText(Dest, "MZ the old build");
        var bad = Downloader(_ => Body("MZ tampered content!"), out _);
        var file = File(mirror: null);
        file.Size = 0;

        Assert.False((await bad.DownloadAsync(file, Dest, null, default)).Ok);
        Assert.Equal("MZ the old build", System.IO.File.ReadAllText(Dest));

        var good = Downloader(_ => Body(Good), out _);
        Assert.True((await good.DownloadAsync(file, Dest, null, default)).Ok);
        Assert.Equal(Good, System.IO.File.ReadAllText(Dest));
    }

    [Fact]
    public async Task The_hash_comparison_ignores_case()
    {
        var dl = Downloader(_ => Body(Good), out _);
        var file = File(mirror: null);
        file.Sha256 = file.Sha256!.ToUpperInvariant();

        Assert.True((await dl.DownloadAsync(file, Dest, null, default)).Ok);
    }
}
