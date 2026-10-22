using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>
/// Older catalog builds on demand: which builds the pickers offer as "(download)", the download into the library, and what it must not
/// touch (the "newest fetched" markers, the default, the Lecram private channel). Fake handler, temp data folder, no network.
/// </summary>
public sealed class CatalogBuildPickerTests : IDisposable
{
    private const string Mirror = "https://github.com/ApolloF/DLSS5-Updater-catalog/releases/download/mirror-2026-10/";

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

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-picker-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ComponentStore _store;
    private readonly AppSettings _settings = new();
    private readonly Dictionary<string, byte[]> _served = [];
    private readonly FakeWeb _web;
    private readonly CatalogDownloader _fetch;

    public CatalogBuildPickerTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
        _store = new ComponentStore(new GitHubClient(), () => true);
        _web = new FakeWeb(u => _served.GetValueOrDefault(u));
        _fetch = new CatalogDownloader(new GitHubClient(_web));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private byte[] NrDll(Version version, string? salt = null)
    {
        var path = Path.Combine(_tmp, "nr-" + Guid.NewGuid().ToString("N")[..6] + ".dll");
        FakeVersionDll.Write(path, version, ComponentStore.DlssNrFile);
        return salt is null ? File.ReadAllBytes(path) : [.. File.ReadAllBytes(path), .. Encoding.ASCII.GetBytes(salt)];
    }

    private static byte[] LecramV11(string version)
    {
        var text = $"RenoDX.DLSS5\0NR-SETTINGS event=effective\0NR-COMPAT v1 coinstalled=0\0{version}\0{version}NR on\0";
        var bytes = new byte[0x200];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        bytes[0x3C] = 0x80;
        "PE\0\0"u8.CopyTo(bytes.AsSpan(0x80));
        return [.. bytes, .. Encoding.ASCII.GetBytes(text)];
    }

    private CatalogNeuralBuild Entry(string name, string version, byte[] bytes, string? generation = null, bool serve = true)
    {
        var sha = Sha(bytes);
        var entry = new CatalogNeuralBuild
        {
            Name = name, Version = version, Sha256 = sha, Size = bytes.Length, Generation = generation,
            MirrorUrl = Mirror + sha[..16] + "-" + name,
        };
        if (serve) _served[entry.MirrorUrl!] = bytes;
        return entry;
    }

    private static CatalogNeuralLine Line(CatalogNeuralBuild current, params CatalogNeuralBuild[] history) => new() { Current = current, History = [.. history] };

    private static CatalogSections Sections(CatalogNeuralLine? dlssnr = null, CatalogNeuralLine? lecram = null, CatalogRenodxDlss? renodx = null) =>
        new() { Neural = new CatalogNeural { Dlssnr = dlssnr, Lecram = lecram }, RenodxDlss = renodx };

    private CatalogDlssBuild RenoDxEntry(Version version, string label)
    {
        var dll = Path.Combine(_tmp, "sf-" + Guid.NewGuid().ToString("N")[..6] + ".bin");
        FakeVersionDll.Write(dll, version, ComponentStore.RenoDxFile);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) zip.CreateEntryFromFile(dll, ComponentStore.RenoDxFile);
        var bytes = ms.ToArray();
        var sha = Sha(bytes);
        var build = new CatalogDlssBuild
        {
            Tag = "renodx-dlss-SF-" + label, Version = label,
            File = new CatalogBinary { Name = "renodx-dlss_SF.zip", Sha256 = sha, Size = bytes.Length, MirrorUrl = Mirror + sha[..16] + "-renodx-dlss_SF.zip" },
        };
        _served[build.File.MirrorUrl!] = bytes;
        return build;
    }

    // ---------- what is offered ----------

    [Fact]
    public void Offers_are_the_catalog_builds_the_library_lacks_newest_first_with_hash_labels_for_shared_versions()
    {
        var newest = Entry(ComponentStore.DlssNrFile, "310.8.3", NrDll(new Version(310, 8, 3, 0)));
        var a = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0), "one"));
        var b = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0), "two"));
        var ownedBytes = NrDll(new Version(310, 7, 0, 0));
        var owned = Entry(ComponentStore.DlssNrFile, "310.7.0", ownedBytes);
        var noHash = new CatalogNeuralBuild { Name = ComponentStore.DlssNrFile, Version = "310.6.0", Sha256 = "nothex", MirrorUrl = Mirror + "x" };
        var noUrl = new CatalogNeuralBuild { Name = ComponentStore.DlssNrFile, Version = "310.5.0", Sha256 = new string('c', 64) };
        var path = Path.Combine(_tmp, "own.dll");
        File.WriteAllBytes(path, ownedBytes);
        _store.ImportDlssNr(path);

        var offers = _store.OfferedBuilds(CatalogBuildKind.DlssNr, Sections(Line(newest, a, b, owned, noHash, noUrl)), false);

        Assert.Equal(["310.8.3", "310.8.0 · " + a.Sha256[..8], "310.8.0 · " + b.Sha256[..8]], offers.Select(o => o.Label));
        Assert.All(offers, o => Assert.EndsWith(" (download)", o.ChoiceLabel));
        Assert.Equal(["catalog:" + newest.Sha256, "catalog:" + a.Sha256, "catalog:" + b.Sha256], offers.Select(o => o.Tag));
        Assert.Equal(newest.Sha256, CatalogOffer.ShaOf(offers[0].Tag));
        Assert.Null(CatalogOffer.ShaOf(newest.Sha256));
    }

    [Fact]
    public void Shared_versions_are_offered_in_catalog_order_with_their_file_version()
    {
        var newest = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0), "one"));
        var older = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0), "two"));
        newest.FileVersion = "0.2026.828.517";
        older.FileVersion = "0.2026.828.400";

        var offers = _store.OfferedBuilds(CatalogBuildKind.DlssNr, Sections(Line(newest, older)), false);

        Assert.Equal(["310.8.0 · 0.2026.828.517", "310.8.0 · 0.2026.828.400"], offers.Select(o => o.Label));
        Assert.Equal("310.8.0 · 0.2026.828.517 (download)", offers[0].ChoiceLabel);
    }

    [Fact]
    public void A_catalog_without_the_section_offers_nothing()
    {
        Assert.Empty(_store.OfferedBuilds(CatalogBuildKind.DlssNr, null, false));
        Assert.Empty(_store.OfferedBuilds(CatalogBuildKind.Lecram, new CatalogSections(), false));
        Assert.Empty(_store.OfferedBuilds(CatalogBuildKind.RenoDx, Sections(), false));
    }

    [Fact]
    public void Lecram_is_offered_from_the_mirror_but_not_while_the_private_channel_is_on()
    {
        var newest = Entry(ComponentStore.Lecram11File, "v11.0.0-beta.6", LecramV11("v11.0.0-beta.6"));
        var older = Entry(ComponentStore.Lecram11File, "v11.0.0-beta.5", LecramV11("v11.0.0-beta.5"));
        var sections = Sections(lecram: Line(newest, older));

        Assert.Equal([newest.Sha256, older.Sha256], _store.OfferedBuilds(CatalogBuildKind.Lecram, sections, false).Select(o => o.Sha256));
        Assert.Empty(_store.OfferedBuilds(CatalogBuildKind.Lecram, sections, true));
        // The other mirror components are not affected by the channel.
        Assert.NotEmpty(_store.OfferedBuilds(CatalogBuildKind.DlssNr, Sections(Line(Entry(ComponentStore.DlssNrFile, "310.8.3", NrDll(new Version(310, 8, 3, 0))))), true));
    }

    [Fact]
    public async Task A_lecram_download_is_refused_while_the_private_channel_is_on()
    {
        var older = Entry(ComponentStore.Lecram11File, "v11.0.0-beta.5", LecramV11("v11.0.0-beta.5"));
        var sections = Sections(lecram: Line(Entry(ComponentStore.Lecram11File, "v11.0.0-beta.6", LecramV11("v11.0.0-beta.6")), older));

        Assert.Null(await _store.DownloadCatalogBuildAsync(CatalogBuildKind.Lecram, sections, older.Sha256, _fetch, null, true, default));

        Assert.Empty(_web.Urls);
        Assert.Empty(_store.LecramBuilds);
    }

    [Fact]
    public async Task ShortFuses_offers_skip_builds_already_downloaded_and_label_shared_versions_with_the_hash()
    {
        var newest = RenoDxEntry(new Version(0, 2026, 1003, 2350), "26.1003.2350");
        var older = RenoDxEntry(new Version(0, 2026, 928, 1), "26.0928.1");
        var twin = RenoDxEntry(new Version(0, 2026, 928, 2), "26.0928.1");
        var section = new CatalogRenodxDlss { Builds = [newest, older, twin, new CatalogDlssBuild { Tag = "broken", Version = "0", File = new CatalogBinary() }] };
        await _store.DownloadCatalogBuildAsync(CatalogBuildKind.RenoDx, Sections(renodx: section), newest.File.Sha256!, _fetch, null, false, default);

        var offers = _store.OfferedBuilds(CatalogBuildKind.RenoDx, Sections(renodx: section), false);

        Assert.Equal(["26.0928.1 · " + older.File.Sha256![..8], "26.0928.1 · " + twin.File.Sha256![..8]], offers.Select(o => o.Label));
    }

    // ---------- downloading one ----------

    [Fact]
    public async Task An_older_dlssnr_joins_the_library_and_leaves_the_markers_and_the_default_alone()
    {
        var newest = Entry(ComponentStore.DlssNrFile, "310.8.3", NrDll(new Version(310, 8, 3, 0)));
        var older = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0)));
        var sections = Sections(Line(newest, older));
        _settings.DlssNrId = "pinned";

        var id = await _store.DownloadCatalogBuildAsync(CatalogBuildKind.DlssNr, sections, older.Sha256, _fetch, null, false, default);

        Assert.Equal(older.Sha256, id);
        Assert.Equal(older.Sha256, Assert.Single(_store.DlssNrVersions).Id);
        Assert.Equal([older.MirrorUrl!], _web.Urls);
        Assert.Null(_settings.CatalogDlssNrFetched);
        Assert.Null(_settings.NewestDlssNrSeen);
        Assert.Equal("pinned", _settings.DlssNrId);
        Assert.Empty(Directory.GetDirectories(AppPaths.Cache, "neural-*"));
        Assert.DoesNotContain(_store.OfferedBuilds(CatalogBuildKind.DlssNr, sections, false), o => o.Sha256 == older.Sha256);
    }

    [Fact]
    public async Task The_next_newest_check_still_fetches_the_newest_and_never_the_older_build_again()
    {
        var newest = Entry(ComponentStore.DlssNrFile, "310.8.3", NrDll(new Version(310, 8, 3, 0)));
        var older = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0)));
        var line = Line(newest, older);
        await _store.DownloadCatalogBuildAsync(CatalogBuildKind.DlssNr, Sections(line), older.Sha256, _fetch, null, false, default);

        var fetched = await _store.CheckDlssNrCatalogAsync(_settings, line, _fetch, null, default);

        Assert.Equal(newest.Sha256, fetched!.Id);
        Assert.Equal(newest.Sha256, _settings.CatalogDlssNrFetched);
        Assert.Equal([older.MirrorUrl!, newest.MirrorUrl!], _web.Urls);
        var requests = _web.Urls.Count;
        Assert.Null(await _store.CheckDlssNrCatalogAsync(_settings, line, _fetch, null, default));
        Assert.Equal(requests, _web.Urls.Count);
    }

    [Fact]
    public async Task Downloading_the_older_build_after_the_newest_does_not_change_what_the_check_remembers()
    {
        var newest = Entry(ComponentStore.DlssNrFile, "310.8.3", NrDll(new Version(310, 8, 3, 0)));
        var older = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0)));
        var line = Line(newest, older);
        await _store.CheckDlssNrCatalogAsync(_settings, line, _fetch, null, default);
        var defaultId = _settings.DlssNrId;

        await _store.DownloadCatalogBuildAsync(CatalogBuildKind.DlssNr, Sections(line), older.Sha256, _fetch, null, false, default);

        Assert.Equal(newest.Sha256, _settings.CatalogDlssNrFetched);
        Assert.Equal(defaultId, _settings.DlssNrId);
        Assert.Equal(2, _store.DlssNrVersions.Count);
    }

    [Fact]
    public async Task It_works_with_the_automatic_download_switched_off()
    {
        var older = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0)));
        _settings.AutoDownloadDlssNr = false;

        Assert.NotNull(await _store.DownloadCatalogBuildAsync(CatalogBuildKind.DlssNr, Sections(Line(older)), older.Sha256, _fetch, null, false, default));
    }

    [Fact]
    public async Task A_download_that_fails_the_hash_check_is_refused_and_leaves_nothing()
    {
        var older = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0)));
        _served[older.MirrorUrl!] = NrDll(new Version(310, 8, 0, 0), "tampered");

        var id = await _store.DownloadCatalogBuildAsync(CatalogBuildKind.DlssNr, Sections(Line(older)), older.Sha256, _fetch, null, false, default);

        Assert.Null(id);
        Assert.Empty(_store.DlssNrVersions);
        Assert.Null(_settings.CatalogDlssNrFetched);
    }

    [Fact]
    public async Task A_build_the_catalog_does_not_offer_and_an_offline_mirror_are_refused_without_throwing()
    {
        var older = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0)));
        var sections = Sections(Line(older));

        Assert.Null(await _store.DownloadCatalogBuildAsync(CatalogBuildKind.DlssNr, sections, new string('9', 64), _fetch, null, false, default));
        Assert.Null(await _store.DownloadCatalogBuildAsync(CatalogBuildKind.DlssNr, null, older.Sha256, _fetch, null, false, default));
        Assert.Empty(_web.Urls);
        _served.Clear();
        Assert.Null(await _store.DownloadCatalogBuildAsync(CatalogBuildKind.DlssNr, sections, older.Sha256, _fetch, null, false, default));
        Assert.Empty(_store.DlssNrVersions);
    }

    [Fact]
    public async Task An_older_lecram_build_goes_by_its_own_generation_and_leaves_the_default_alone()
    {
        var v10File = Path.Combine(_tmp, "v10.addon64");
        FakeVersionDll.Write(v10File, new Version(10, 7, 0, 0), ComponentStore.Lecram10File);
        // The catalog calls it v11; the file says v10 and wins.
        var oldV10 = Entry(ComponentStore.Lecram10File, "v10.7.0", File.ReadAllBytes(v10File), generation: "v11");
        var newest = Entry(ComponentStore.Lecram11File, "v11.0.0-beta.6", LecramV11("v11.0.0-beta.6"), generation: "v11");
        var sections = Sections(lecram: Line(newest, oldV10));

        var id = await _store.DownloadCatalogBuildAsync(CatalogBuildKind.Lecram, sections, oldV10.Sha256, _fetch, null, false, default);

        var build = Assert.Single(_store.LecramBuilds);
        Assert.Equal(id, build.Id);
        Assert.Equal(LecramGeneration.V10, build.Generation);
        Assert.True(build.Downloaded);
        Assert.Null(_settings.LecramId);
        Assert.Null(_settings.CatalogLecramFetched);
        Assert.Equal(new AppSettings().LecramLastResult, _settings.LecramLastResult);
    }

    [Fact]
    public async Task An_older_shortfuse_build_is_stamped_with_its_catalog_hash_and_the_default_stays()
    {
        var newest = RenoDxEntry(new Version(0, 2026, 1003, 2350), "26.1003.2350");
        var older = RenoDxEntry(new Version(0, 2026, 928, 1), "26.0928.1");
        var sections = Sections(renodx: new CatalogRenodxDlss { Builds = [newest, older] });
        _settings.RenoDxId = "pinned";

        var id = await _store.DownloadCatalogBuildAsync(CatalogBuildKind.RenoDx, sections, older.File.Sha256!, _fetch, null, false, default);

        var build = Assert.Single(_store.RenoDxBuilds);
        Assert.Equal(id, build.Id);
        Assert.Equal(older.File.Sha256, build.CatalogSha);
        Assert.Equal("pinned", _settings.RenoDxId);
        // The newest check still sees only the newest as missing and does not touch the default pick either.
        var fetched = await _store.CheckRenoDxCatalogAsync(_settings, sections.RenodxDlss, _fetch, null, default);
        Assert.Equal(newest.File.Sha256, fetched!.CatalogSha);
        Assert.Empty(_store.OfferedBuilds(CatalogBuildKind.RenoDx, sections, false));
    }

    // ---------- cleanup ----------

    [Fact]
    public async Task A_picked_older_build_survives_the_cleanup_and_goes_like_any_download_once_it_loses_its_pick()
    {
        var b4 = Entry(ComponentStore.Lecram11File, "v11.0.0-beta.4", LecramV11("v11.0.0-beta.4"));
        var b5 = Entry(ComponentStore.Lecram11File, "v11.0.0-beta.5", LecramV11("v11.0.0-beta.5"));
        var b6 = Entry(ComponentStore.Lecram11File, "v11.0.0-beta.6", LecramV11("v11.0.0-beta.6"));
        var sections = Sections(lecram: Line(b6, b5, b4));
        foreach (var e in new[] { b4, b5, b6 }) await _store.DownloadCatalogBuildAsync(CatalogBuildKind.Lecram, sections, e.Sha256, _fetch, null, false, default);
        _settings.KeepBuilds[LibraryKind.Lecram.ToString()] = 1;
        _settings.For("game-a").LecramId = b4.Sha256;

        var planned = _store.CleanupCandidates(LibraryKind.Lecram, _settings, [], downloadsOnly: true).Select(b => b.Id).ToList();

        Assert.Equal([b5.Sha256], planned);

        _settings.For("game-a").LecramId = null;
        Assert.Equal([b5.Sha256, b4.Sha256], _store.CleanupCandidates(LibraryKind.Lecram, _settings, [], downloadsOnly: true).Select(b => b.Id));
    }

    // ---------- the game page ----------

    private GameViewModel NewGame(Func<CatalogBuildKind, string, Action<string>, Task<bool>>? pick, CatalogSections sections)
    {
        File.WriteAllText(AppPaths.CatalogFile, JsonSerializer.Serialize(new CatalogDocument { Schema = 1, Generated = "2026-10-22T00:00:00Z", Sections = sections }, JsonCtx.Default.CatalogDocument));
        var catalog = new CatalogState(new CatalogClient(new GitHubClient()));
        catalog.LoadCached();
        var services = new Services { Settings = _settings, Store = _store, Installer = new Installer(_store), Catalog = catalog, PickCatalogBuild = pick };
        var root = Path.Combine(_tmp, "vm-game");
        Directory.CreateDirectory(root);
        return new GameViewModel(new GameInfo { Id = root, Root = root, Name = "VM game" }, services);
    }

    private static async Task WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(25);
        Assert.True(done());
    }

    [Fact]
    public async Task The_game_page_lists_the_downloads_after_the_library_and_a_pick_downloads_then_selects_it_for_that_game_only()
    {
        var newest = Entry(ComponentStore.DlssNrFile, "310.8.3", NrDll(new Version(310, 8, 3, 0)));
        var older = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0)));
        var sections = Sections(Line(newest, older));
        var local = Path.Combine(_tmp, "local.dll");
        File.WriteAllBytes(local, NrDll(new Version(310, 7, 0, 0)));
        var own = _store.ImportDlssNr(local);
        var vm = NewGame(async (kind, tag, select) =>
        {
            var id = await _store.DownloadCatalogBuildAsync(kind, sections, CatalogOffer.ShaOf(tag)!, _fetch, null, false, default);
            if (id is null) return false;
            select(id);
            return true;
        }, sections);

        Assert.Equal([null, own.Id, "catalog:" + newest.Sha256, "catalog:" + older.Sha256], vm.NrChoices.Select(c => c.Tag));
        Assert.Equal("310.8.0 (download)", vm.NrChoices[^1].Label);

        vm.NrChoice = vm.NrChoices[^1];
        await WaitFor(() => _settings.Games.GetValueOrDefault(vm.Id)?.DlssNrId == older.Sha256);
        vm.RebuildNrChoices();

        Assert.Equal(older.Sha256, vm.NrChoice!.Tag);
        Assert.Null(_settings.DlssNrId);
        Assert.Equal(["catalog:" + newest.Sha256], vm.NrChoices.Where(c => CatalogOffer.IsOffer(c.Tag)).Select(c => c.Tag));
    }

    [Fact]
    public async Task A_download_that_fails_puts_the_game_page_back_to_its_saved_pick()
    {
        var older = Entry(ComponentStore.DlssNrFile, "310.8.0", NrDll(new Version(310, 8, 0, 0)), serve: false);
        var sections = Sections(Line(Entry(ComponentStore.DlssNrFile, "310.8.3", NrDll(new Version(310, 8, 3, 0))), older));
        var vm = NewGame(async (kind, tag, select) =>
        {
            var id = await _store.DownloadCatalogBuildAsync(kind, sections, CatalogOffer.ShaOf(tag)!, _fetch, null, false, default);
            if (id is null) return false;
            select(id);
            return true;
        }, sections);
        var offer = vm.NrChoices.First(c => c.Tag == "catalog:" + older.Sha256);

        vm.NrChoice = offer;
        await WaitFor(() => vm.NrChoice?.Tag is null);

        Assert.Single(_web.Urls);
        Assert.Null(_settings.Games.GetValueOrDefault(vm.Id)?.DlssNrId);
        Assert.Empty(_store.DlssNrVersions);
    }

    [Fact]
    public async Task Lecram_and_shortfuse_picks_on_the_game_page_save_the_library_id()
    {
        var lecram = Entry(ComponentStore.Lecram11File, "v11.0.0-beta.5", LecramV11("v11.0.0-beta.5"));
        var sf = RenoDxEntry(new Version(0, 2026, 928, 1), "26.0928.1");
        var sections = Sections(lecram: Line(lecram), renodx: new CatalogRenodxDlss { Builds = [sf] });
        var vm = NewGame(async (kind, tag, select) =>
        {
            var id = await _store.DownloadCatalogBuildAsync(kind, sections, CatalogOffer.ShaOf(tag)!, _fetch, null, false, default);
            if (id is null) return false;
            select(id);
            return true;
        }, sections);

        vm.LecramChoice = vm.LecramChoices.Single(c => CatalogOffer.IsOffer(c.Tag));
        vm.RenoDxChoice = vm.RenoDxChoices.Single(c => CatalogOffer.IsOffer(c.Tag));
        await WaitFor(() => _settings.Games.GetValueOrDefault(vm.Id) is { LecramId: not null, RenoDxId: not null });

        var over = _settings.Games[vm.Id];
        Assert.Equal(_store.LecramBuilds.Single().Id, over.LecramId);
        Assert.Equal(_store.RenoDxBuilds.Single().Id, over.RenoDxId);
        Assert.Null(_settings.LecramId);
        Assert.Null(_settings.RenoDxId);
    }

    [Fact]
    public void Without_a_catalog_the_pickers_show_the_library_only()
    {
        var vm = NewGame(null, new CatalogSections());

        Assert.DoesNotContain(vm.NrChoices, c => CatalogOffer.IsOffer(c.Tag));
        Assert.DoesNotContain(vm.LecramChoices, c => CatalogOffer.IsOffer(c.Tag));
        Assert.DoesNotContain(vm.RenoDxChoices, c => CatalogOffer.IsOffer(c.Tag));
    }
}
