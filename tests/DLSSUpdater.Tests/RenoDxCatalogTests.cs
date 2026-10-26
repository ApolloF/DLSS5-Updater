using System.IO.Compression;
using System.Net;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>
/// ShortFuse's renodx-dlss add-on from the catalog (design.md section 3, the exception): a hash-pinned zip is downloaded, refused when it
/// doesn't match, and the add-on inside goes into the library like an import. Fake handler, no network.
/// </summary>
public sealed class RenoDxCatalogTests : IDisposable
{
    private sealed class FakeWeb(Func<string, byte[]?> body) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            return Task.FromResult(body(url) is { } bytes
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-sfcat-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ComponentStore _store;
    private readonly AppSettings _settings = new();
    private readonly Dictionary<string, byte[]> _served = [];

    public RenoDxCatalogTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
        _store = new ComponentStore(new GitHubClient(), () => true);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private byte[] Zip(Version version, string entryName = "renodx-dlss.addon64", string original = "renodx-dlss.addon64")
    {
        var dll = Path.Combine(_tmp, "build-" + Guid.NewGuid().ToString("N")[..6] + ".bin");
        FakeVersionDll.Write(dll, version, original);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            zip.CreateEntryFromFile(dll, entryName);
        return ms.ToArray();
    }

    private (CatalogRenodxDlss Section, FakeWeb Web, CatalogDownloader Downloader) Serve(byte[] zip, string tag = "renodx-dlss-SF-26.1003.2350", string? sha = null)
    {
        var url = "https://github.com/RankFTW/rhi-repo/releases/download/" + tag + "/renodx-dlss_SF.zip";
        _served[url] = zip;
        var web = new FakeWeb(u => _served.GetValueOrDefault(u));
        var section = new CatalogRenodxDlss
        {
            Builds =
            [
                new CatalogDlssBuild
                {
                    Tag = tag, Version = tag["renodx-dlss-SF-".Length..],
                    File = new CatalogBinary
                    {
                        Name = "renodx-dlss_SF.zip", Url = url, Sha256 = sha ?? System.Security.Cryptography.SHA256.HashData(zip).Aggregate("", (a, b) => a + b.ToString("x2")),
                        Size = zip.Length, Version = "2026-10-21-abcdef0",
                    },
                },
            ],
        };
        return (section, web, new CatalogDownloader(new GitHubClient(web)));
    }

    [Fact]
    public async Task The_newest_catalog_build_lands_in_the_library_as_a_download_and_becomes_the_default()
    {
        var (section, web, dl) = Serve(Zip(new Version(0, 2026, 1003, 2350)));

        var build = await _store.CheckRenoDxCatalogAsync(_settings, section, dl, null, default);

        Assert.NotNull(build);
        Assert.True(build!.Downloaded);
        Assert.Equal(section.Builds[0].File.Sha256, build.CatalogSha);
        Assert.Equal(build.Id, _settings.RenoDxId);
        Assert.Single(_store.RenoDxBuilds);
        Assert.True(File.Exists(ComponentStore.AddonPathOf(AddonKind.RenoDx, build)));
        // Pinned, not mirrored: the author's URL is what was asked.
        Assert.Equal([section.Builds[0].File.Url], web.Urls);
        Assert.Empty(Directory.GetDirectories(AppPaths.Cache, "renodx-dlss-*"));
    }

    [Fact]
    public async Task A_build_the_library_already_has_is_not_downloaded_again()
    {
        var (section, web, dl) = Serve(Zip(new Version(0, 2026, 1003, 2350)));
        await _store.CheckRenoDxCatalogAsync(_settings, section, dl, null, default);
        var requests = web.Urls.Count;

        var again = await _store.CheckRenoDxCatalogAsync(_settings, section, dl, null, default);

        Assert.Null(again);
        Assert.Equal(requests, web.Urls.Count);
    }

    [Fact]
    public async Task A_zip_that_does_not_match_the_catalogs_hash_is_refused_and_leaves_nothing()
    {
        var (section, _, dl) = Serve(Zip(new Version(0, 2026, 1003, 2350)), sha: new string('0', 64));

        var build = await _store.CheckRenoDxCatalogAsync(_settings, section, dl, null, default);

        Assert.Null(build);
        Assert.Empty(_store.RenoDxBuilds);
        Assert.Null(_settings.RenoDxId);
    }

    [Fact]
    public async Task Offline_or_a_missing_download_changes_nothing_and_does_not_throw()
    {
        var (section, _, dl) = Serve(Zip(new Version(0, 2026, 1003, 2350)));
        section.Builds[0].File.Url = "https://github.com/RankFTW/rhi-repo/releases/download/gone/x.zip";

        Assert.Null(await _store.CheckRenoDxCatalogAsync(_settings, section, dl, null, default));
        Assert.Null(await _store.CheckRenoDxCatalogAsync(_settings, null, dl, null, default));
        Assert.Null(await _store.CheckRenoDxCatalogAsync(_settings, new CatalogRenodxDlss(), dl, null, default));
        Assert.Empty(_store.RenoDxBuilds);
    }

    [Fact]
    public async Task The_settings_switch_turns_the_download_off_without_a_request()
    {
        var (section, web, dl) = Serve(Zip(new Version(0, 2026, 1003, 2350)));
        _settings.AutoDownloadRenoDx = false;

        Assert.Null(await _store.CheckRenoDxCatalogAsync(_settings, section, dl, null, default));

        Assert.Empty(web.Urls);
    }

    [Fact]
    public async Task A_zip_without_the_add_on_or_with_the_wrong_one_is_refused()
    {
        var (none, _, dl1) = Serve(Zip(new Version(0, 2026, 1, 1), entryName: "readme.txt"));
        var (other, _, dl2) = Serve(Zip(new Version(0, 2026, 1, 1), original: "renodx-dlssfix.addon64"), tag: "renodx-dlss-SF-26.1004.0000");

        Assert.Null(await _store.CheckRenoDxCatalogAsync(_settings, none, dl1, null, default));
        Assert.Null(await _store.CheckRenoDxCatalogAsync(_settings, other, dl2, null, default));
        Assert.Empty(_store.RenoDxBuilds);
    }

    [Fact]
    public async Task A_file_the_user_imported_already_stays_an_import_and_is_not_fetched_twice()
    {
        var bytes = Zip(new Version(0, 2026, 1003, 2350));
        var imported = Path.Combine(_tmp, "imported", "renodx-dlss.addon64");
        Directory.CreateDirectory(Path.GetDirectoryName(imported)!);
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
            zip.Entries[0].ExtractToFile(imported);
        var mine = _store.ImportRenoDx(imported);
        var (section, web, dl) = Serve(bytes);

        var build = await _store.CheckRenoDxCatalogAsync(_settings, section, dl, null, default);

        Assert.Equal(mine.Id, build!.Id);
        Assert.False(build.Downloaded);
        Assert.Single(_store.RenoDxBuilds);
        var requests = web.Urls.Count;
        Assert.Null(await _store.CheckRenoDxCatalogAsync(_settings, section, dl, null, default));
        Assert.Equal(requests, web.Urls.Count);
    }

    [Fact]
    public async Task A_newer_catalog_build_moves_the_default_and_an_older_one_does_not()
    {
        var (first, _, dl) = Serve(Zip(new Version(0, 2026, 1003, 2350)), "renodx-dlss-SF-26.1003.2350");
        await _store.CheckRenoDxCatalogAsync(_settings, first, dl, null, default);
        var oldDefault = _settings.RenoDxId;

        var (older, _, dlOlder) = Serve(Zip(new Version(0, 2026, 928, 205)), "renodx-dlss-SF-26.0928.0205");
        await _store.CheckRenoDxCatalogAsync(_settings, older, dlOlder, null, default);
        Assert.Equal(oldDefault, _settings.RenoDxId);

        var (newer, _, dlNewer) = Serve(Zip(new Version(0, 2026, 1016, 1704)), "renodx-dlss-SF-26.1016.1704");
        var build = await _store.CheckRenoDxCatalogAsync(_settings, newer, dlNewer, null, default);

        Assert.Equal(build!.Id, _settings.RenoDxId);
        Assert.Equal(3, _store.RenoDxBuilds.Count);
    }
}
