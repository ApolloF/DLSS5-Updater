using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public async Task A_file_the_user_imported_already_stays_an_import_and_is_not_fetched()
    {
        var bytes = Zip(new Version(0, 2026, 1003, 2350));
        var imported = Path.Combine(_tmp, "imported", "renodx-dlss.addon64");
        Directory.CreateDirectory(Path.GetDirectoryName(imported)!);
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
            zip.Entries[0].ExtractToFile(imported);
        var mine = _store.ImportRenoDx(imported);
        var (section, web, dl) = Serve(bytes);

        Assert.Null(await _store.CheckRenoDxCatalogAsync(_settings, section, dl, null, default));

        Assert.Empty(web.Urls);
        Assert.False(Assert.Single(_store.RenoDxBuilds).Downloaded);
        Assert.Equal(mine.Id, _store.RenoDxBuilds[0].Id);
    }

    // ---------- version label and the picker list ----------

    /// <summary>An add-on whose About section shows "Build: <paramref name="buildTime"/>", as ShortFuse's builds do.</summary>
    private string Addon(Version fileVersion, string? buildTime)
    {
        var path = Path.Combine(_tmp, "addon-" + Guid.NewGuid().ToString("N")[..6], ComponentStore.RenoDxFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FakeVersionDll.Write(path, fileVersion, ComponentStore.RenoDxFile);
        if (buildTime is not null)
            using (var file = new FileStream(path, FileMode.Append))
                file.Write(Encoding.ASCII.GetBytes($"ShortFuse's Ko-Fi\0\0{buildTime}\0Build: \0About\0"));
        return path;
    }

    private static CatalogDlssBuild Listed(string version) => new()
    {
        Tag = "renodx-dlss-SF-" + version, Version = version,
        File = new CatalogBinary
        {
            Name = "renodx-dlss_SF_" + version + ".zip", Url = "https://github.com/RankFTW/rhi-repo/releases/download/x/" + version + ".zip",
            Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(version))).ToLowerInvariant(),
        },
    };

    [Fact]
    public void The_label_is_the_build_time_the_add_on_shows_written_like_the_mirrors_releases()
    {
        static string Label(string text, string fileVersion) => ComponentStore.RenoDxBuildLabel(Encoding.ASCII.GetBytes(text), fileVersion);

        Assert.Equal("26.0928.0205", Label("Ko-Fi\0\02026-09-28T02:05:38\0Build: \0About", "0.2026.928.306"));
        // Without the caption, or with something else before it, the file version stands in, in the same form.
        Assert.Equal("26.0928.0306", Label("Ko-Fi\0\02026-09-28T02:05:38\0About", "0.2026.928.306"));
        Assert.Equal("26.1016.1704", Label("not a time\0Build: \0", "0.2026.1016.1704"));
        Assert.Equal("26.0101.0000", Label("", "0.2026.101.0"));
        // A file version that is no date stays as it is.
        Assert.Equal("1.2.3.4", Label("", "1.2.3.4"));
        Assert.Equal("0.2026.1399.0", Label("", "0.2026.1399.0"));
    }

    [Fact]
    public void An_import_is_labelled_by_its_build_time_without_the_import_date()
    {
        var build = _store.ImportRenoDx(Addon(new Version(0, 2026, 928, 306), "2026-09-28T02:05:38"));

        Assert.Equal("0.2026.928.306", build.FileVersion);
        Assert.Equal("26.0928.0205", build.DetectedLabel);
        Assert.Equal("26.0928.0205", build.Label);
    }

    [Fact]
    public void A_build_an_older_version_labelled_by_its_file_version_gets_its_build_time()
    {
        var build = _store.ImportRenoDx(Addon(new Version(0, 2026, 1003, 2333), "2026-10-03T23:50:12"));
        var record = Path.Combine(AppPaths.Components, "renodx-dlss", build.Id, "version.json");
        File.WriteAllText(record, JsonSerializer.Serialize(build with { DetectedLabel = "0.2026.1003.2333" }, JsonCtx.Default.AddonBuild));

        var reread = Assert.Single(new ComponentStore(new GitHubClient(), () => true).RenoDxBuilds);

        Assert.Equal("26.1003.2350", reread.DetectedLabel);
        Assert.Equal("26.1003.2350", JsonSerializer.Deserialize(File.ReadAllText(record), JsonCtx.Default.AddonBuild)!.DetectedLabel);
    }

    [Fact]
    public async Task A_build_imported_by_hand_is_neither_offered_nor_fetched_again()
    {
        // The owner's case: the 0928 build imported by hand, the catalog naming the same build 26.0928.0205.
        _store.ImportRenoDx(Addon(new Version(0, 2026, 928, 306), "2026-09-28T02:05:38"));
        var same = Listed("26.0928.0205");
        var section = new CatalogRenodxDlss { Builds = [Listed("26.1003.2350"), same] };

        var offers = _store.OfferedBuilds(CatalogBuildKind.RenoDx, new CatalogSections { RenodxDlss = section }, false);

        Assert.Equal(["26.1003.2350"], offers.Select(o => o.Label));
        var (newest, web, dl) = Serve(Zip(new Version(0, 2026, 928, 306)), "renodx-dlss-SF-26.0928.0205");
        Assert.Null(await _store.CheckRenoDxCatalogAsync(_settings, newest, dl, null, default));
        Assert.Empty(web.Urls);
    }

    [Fact]
    public void Only_the_newest_five_builds_the_library_lacks_are_offered()
    {
        _store.ImportRenoDx(Addon(new Version(0, 2026, 922, 141), "2026-09-22T00:41:00"));
        var section = new CatalogRenodxDlss
        {
            Builds = [.. new[] { "0.55", "26.0917.1145", "26.1003.2350", "26.0928.0205", "26.0927.2125", "26.0922.0041", "26.0919.2025", "26.0917.1904", "0.1" }
                .Select(Listed)],
        };

        var offers = _store.OfferedBuilds(CatalogBuildKind.RenoDx, new CatalogSections { RenodxDlss = section }, false);

        Assert.Equal(ComponentStore.MaxRenoDxOffers, offers.Count);
        Assert.Equal(["26.1003.2350", "26.0928.0205", "26.0927.2125", "26.0919.2025", "26.0917.1904"], offers.Select(o => o.Label));
    }

    [Fact]
    public void The_picker_lists_library_builds_and_downloads_in_one_format_newest_first()
    {
        _store.ImportRenoDx(Addon(new Version(0, 2026, 1016, 1704), "2026-10-16T13:04:22"));
        _store.ImportRenoDx(Addon(new Version(0, 2026, 1003, 2333), "2026-10-03T23:50:12"));
        _store.ImportRenoDx(Addon(new Version(0, 2026, 928, 306), "2026-09-28T02:05:38"));
        var section = new CatalogRenodxDlss { Builds = [.. new[] { "26.1003.2350", "26.0928.0205", "26.0927.2125", "0.55" }.Select(Listed)] };
        var offers = _store.OfferedBuilds(CatalogBuildKind.RenoDx, new CatalogSections { RenodxDlss = section }, false);

        var list = BuildPickerList.Merge(_store.RenoDxBuilds.Select(b => b.ToPickerEntry()), offers.Select(o => o.ToEntry()));

        Assert.Equal(["2026-10-16 13:04", "2026-10-03 23:50", "2026-09-28 02:05", "2026-09-27 21:25 · download", "0.55 · download"], list.Select(c => c.Label));
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
