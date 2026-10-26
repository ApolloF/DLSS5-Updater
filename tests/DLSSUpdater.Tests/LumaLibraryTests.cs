using System.Net;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>The Luma library: hash-checked download, import, de-duplication, removal, the per-game pin and which zip entries are placed.</summary>
public class LumaLibraryTests : IDisposable
{
    private sealed class FakeWeb(Func<string, byte[]?> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(body(request.RequestUri!.ToString()) is { } bytes
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-lumalib-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ComponentStore _store;

    public LumaLibraryTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _store = new ComponentStore(new GitHubClient(), () => true);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    private CatalogDownloader Downloader(byte[]? served) => new(new GitHubClient(new FakeWeb(url => url.EndsWith("Luma-Prey.zip") ? served : null)));

    private string ZipFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_tmp, "in", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static CatalogBinary PreyFile() => LumaFixtures.Section().PackageOf("Prey")!.FileFor(64)!;

    [Fact]
    public async Task A_catalog_zip_is_downloaded_checked_and_recorded_with_the_release_tag()
    {
        var build = await _store.EnsureLumaBuildAsync(PreyFile(), "Prey", "latest-755", Downloader(LumaFixtures.PreyZip), null, default);

        Assert.Equal(LumaFixtures.Sha(LumaFixtures.PreyZip), build.Id);
        Assert.Equal("latest-755", build.Version);
        Assert.Equal("Prey", build.PackageId);
        Assert.Equal(LumaFixtures.PreyZip, File.ReadAllBytes(ComponentStore.LumaPathOf(build)));
        Assert.NotNull(_store.LumaBuildFor(build.Id));
        Assert.Single(_store.LumaBuilds);
        Assert.Contains(_store.Library(LibraryKind.Luma), b => b.Id == build.Id && b.Group == "Prey");
    }

    [Fact]
    public async Task A_zip_whose_hash_differs_from_the_catalog_is_refused_and_leaves_nothing()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.EnsureLumaBuildAsync(PreyFile(), "Prey", "latest-755", Downloader(LumaFixtures.PreyZipV2), null, default));

        Assert.NotEmpty(ex.Message);
        Assert.Empty(_store.LumaBuilds);
    }

    [Fact]
    public async Task A_second_ensure_of_the_same_zip_downloads_nothing()
    {
        await _store.EnsureLumaBuildAsync(PreyFile(), "Prey", "latest-755", Downloader(LumaFixtures.PreyZip), null, default);

        var again = await _store.EnsureLumaBuildAsync(PreyFile(), "Prey", "latest-755", Downloader(null), null, default);

        Assert.Equal(LumaFixtures.Sha(LumaFixtures.PreyZip), again.Id);
    }

    [Fact]
    public void An_imported_zip_is_kept_under_custom_with_its_own_hash_and_importing_it_again_adds_nothing()
    {
        var path = ZipFile("Luma-Mine.zip", LumaFixtures.Zip("Luma-Mine.addon64"));

        var build = _store.ImportLumaBuild(path);
        var again = _store.ImportLumaBuild(path);

        Assert.Equal(build.Id, again.Id);
        Assert.True(build.Imported);
        Assert.Equal("custom", build.PackageId);
        Assert.Equal("x64", build.Arch);
        Assert.StartsWith("imported ", build.Version);
        Assert.Single(_store.LumaBuilds);
    }

    [Fact]
    public void An_imported_zip_with_a_32_bit_add_on_is_a_32_bit_build()
    {
        var build = _store.ImportLumaBuild(ZipFile("Luma-Old.zip", LumaFixtures.Zip("Luma-Old.addon32")));

        Assert.Equal("x86", build.Arch);
    }

    [Fact]
    public void Imports_without_an_add_on_or_that_are_not_zips_are_refused()
    {
        var empty = ZipFile("Luma-None.zip", System.Text.Encoding.ASCII.GetBytes("not a zip"));
        var noAddon = Path.Combine(_tmp, "in", "Luma-NoAddon.zip");
        using (var z = System.IO.Compression.ZipFile.Open(noAddon, System.IO.Compression.ZipArchiveMode.Create)) z.CreateEntry("readme.txt");

        Assert.Throws<InvalidDataException>(() => _store.ImportLumaBuild(empty));
        var ex = Assert.Throws<InvalidDataException>(() => _store.ImportLumaBuild(noAddon));
        Assert.Contains("no ReShade add-on", ex.Message);
        Assert.Throws<InvalidDataException>(() => _store.ImportLumaBuild(ZipFile("Luma.rar", [1, 2, 3])));
        Assert.Empty(_store.LumaBuilds);
    }

    [Fact]
    public async Task Remove_deletes_the_build_and_its_unpacked_folder()
    {
        var build = await _store.EnsureLumaBuildAsync(PreyFile(), "Prey", "latest-755", Downloader(LumaFixtures.PreyZip), null, default);
        Directory.CreateDirectory(ComponentStore.LumaPackageDirOf(build));

        _store.RemoveLuma(build.Id);

        Assert.Empty(_store.LumaBuilds);
        Assert.False(Directory.Exists(Path.GetDirectoryName(ComponentStore.LumaPathOf(build))));
        Assert.Throws<InvalidDataException>(() => _store.RemoveLuma("../x"));
    }

    [Fact]
    public async Task A_pinned_build_replaces_the_catalog_file_and_a_changed_stored_zip_is_refused()
    {
        var build = await _store.EnsureLumaBuildAsync(PreyFile(), "Prey", "latest-700", Downloader(LumaFixtures.PreyZip), null, default);
        var newest = LumaFixtures.Picker(LumaFixtures.PreyZipV2).Pick(LumaFixtures.Plain("Prey"));

        var pinned = LumaPin.Apply(newest, build.Id, _store.LumaBuilds);

        Assert.Equal(build.Id, pinned.File!.Sha256);
        Assert.Equal("latest-700", pinned.Version);
        Assert.Equal(build, pinned.Pinned);
        Assert.Equal(newest.File!.Sha256, LumaPin.Apply(newest, "unknown", _store.LumaBuilds).File!.Sha256);

        File.WriteAllText(ComponentStore.LumaPathOf(build), "tampered");
        Assert.Throws<InvalidDataException>(() => _store.VerifiedLumaBuild(build));
    }

    [Theory]
    [InlineData("Luma-Prey.addon64", true)]
    [InlineData("Luma-Prey.addon32", true)]
    [InlineData("Luma-Prey.addon", true)]
    [InlineData("Luma/Shaders/main.hlsl", true)]
    [InlineData("Luma/d3dcompiler_47.dll", true)]
    [InlineData("Luma/nvngx_dlss.dll", false)]
    [InlineData("nvngx_dlss.dll", false)]
    [InlineData("dxgi.dll", false)]
    [InlineData("D3D11.dll", false)]
    [InlineData("d3d12.dll", false)]
    [InlineData("opengl32.dll", false)]
    [InlineData("ReShade.ini", false)]
    [InlineData("ReShade64.log", false)]
    [InlineData("Luma/dxgi.dll", true)]
    public void The_zip_filter_skips_ReShade_and_DLSS_files_only(string entry, bool placed)
    {
        Assert.Equal(placed, LumaFiles.Include(entry));
    }

    [Fact]
    public void Add_ons_go_to_the_add_on_folder_and_everything_else_beside_the_exe()
    {
        Assert.Equal(@"C:\G\addons\Luma-Prey.addon64", LumaFiles.Destination("Luma-Prey.addon64", @"C:\G", @"C:\G\addons"));
        Assert.Equal(@"C:\G\Luma\Shaders\main.hlsl", LumaFiles.Destination("Luma/Shaders/main.hlsl", @"C:\G", @"C:\G\addons"));
    }
}
