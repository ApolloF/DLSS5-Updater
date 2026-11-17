using System.IO.Compression;
using System.Text;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// HSF (import only, private licence): the import of a release zip or folder with synthetic files, the layout check, the install in a temp
/// game (64-bit and 32-bit add-on, HSF_streamline, shaders), remove with the original put back, and the method advisor's offer. No network.
/// </summary>
public sealed class HsfTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-hsf-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _target, _releases;
    private readonly ComponentStore _store;
    private readonly HsfService _service;

    public HsfTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _releases = Path.Combine(_tmp, "releases");
        Directory.CreateDirectory(_releases);
        _root = Path.Combine(_tmp, "Game");
        _target = _root;
        _store = new ComponentStore(new GitHubClient(), () => true);
        _service = new HsfService(_store, new Installer(_store));
        UseReShade("6.8.0.2155");
        Write(Path.Combine(_root, "game.exe"), "EXE");
    }

    private void UseReShade(string version)
    {
        var build = _store.ImportReShade(ReShadeFixtures.Folder(Path.Combine(_tmp, "rs-" + version), "a", version));
        _store.SelectedReShade = () => build.Id;
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_tmp, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_tmp, true);
        }
        catch (IOException) { }
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static readonly string[] Package =
    [
        "hsf.addon64", "hsf.addon32", "HSF_streamline/hsf-dlssg-helper64.exe", "HSF_streamline/nvngx_dlssg.dll", "HSF_streamline/sl.common.dll",
        "HSF_streamline/sl.dlss_g.dll", "HSF_streamline/sl.interposer.dll", "HSF_streamline/sl.pcl.dll", "HSF_streamline/sl.reflex.dll",
        "reshade-shaders/Shaders/HSF.fx", "README.txt", "LICENSE.txt", "licenses/Streamline-license.txt",
    ];

    private static string Content(string tag, string file) => $"{file}@{tag}";

    private string Folder(string name, string tag, string[]? skip = null, string? inner = null)
    {
        var root = Path.Combine(_releases, name);
        var dir = inner is null ? root : Path.Combine(root, inner);
        foreach (var f in Package.Where(f => skip?.Contains(f) != true))
            Write(Path.Combine(dir, f.Replace('/', Path.DirectorySeparatorChar)), Content(tag, f));
        return root;
    }

    private string Zip(string fileName, string tag, string[]? skip = null, string prefix = "")
    {
        var path = Path.Combine(_releases, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var f in Package.Where(f => skip?.Contains(f) != true))
        {
            using var w = new StreamWriter(zip.CreateEntry(prefix + f).Open(), new UTF8Encoding(false));
            w.Write(Content(tag, f));
        }
        return path;
    }

    private static string Sha(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    // ---------- import ----------

    [Fact]
    public void Import_a_zip_keeps_the_package_files_and_names_the_build_by_the_version_in_the_file_name()
    {
        var build = HsfLibrary.Import(Zip("HSF-0.9.4.zip", "a"));

        Assert.Equal(Sha(Content("a", "hsf.addon64")), build.Id);
        Assert.Equal("0.9.4", build.Label);
        var pkg = HsfLibrary.PackageOf(build);
        foreach (var f in new[] { "hsf.addon64", "hsf.addon32", "HSF_streamline/sl.interposer.dll", "reshade-shaders/Shaders/HSF.fx" })
            Assert.Equal(Content("a", f), File.ReadAllText(Path.Combine(pkg, f.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(pkg, "README.txt")));
        Assert.False(File.Exists(Path.Combine(pkg, "LICENSE.txt")));
        Assert.Contains(_store.Library(LibraryKind.Hsf), b => b.Id == build.Id && !b.Downloaded);
    }

    [Fact]
    public void Import_a_zip_with_one_folder_around_the_release()
    {
        var build = HsfLibrary.Import(Zip("HSF-0.9.5.zip", "b", prefix: "HSF-0.9.5/"));

        Assert.Equal("0.9.5", build.Label);
        Assert.True(File.Exists(Path.Combine(HsfLibrary.PackageOf(build), "HSF_streamline", "sl.interposer.dll")));
    }

    [Fact]
    public void Import_a_folder_or_a_folder_holding_it_or_the_add_on_file_and_label_imported_without_a_version()
    {
        var plain = HsfLibrary.Import(Folder("release", "c"));
        var viaFile = HsfLibrary.Import(Path.Combine(Folder("rel-b", "d"), "hsf.addon64"));
        var wrapped = HsfLibrary.Import(Folder("v0.9.4-wrap", "e", inner: "HSF"));

        Assert.Equal("imported", plain.Label);
        Assert.Equal("imported", viaFile.Label);
        Assert.Equal("imported", wrapped.Label);
        Assert.Equal(3, HsfLibrary.Builds().Count);
    }

    [Fact]
    public void Importing_the_same_add_on_again_replaces_the_build_instead_of_adding_one()
    {
        HsfLibrary.Import(Zip("HSF-0.9.4.zip", "a"));
        HsfLibrary.Import(Folder("HSF-0.9.4", "a"));

        Assert.Single(HsfLibrary.Builds());
    }

    [Fact]
    public void The_shaders_are_optional_on_import()
    {
        var build = HsfLibrary.Import(Folder("HSF-0.9.4", "a", skip: ["reshade-shaders/Shaders/HSF.fx"]));

        Assert.False(Directory.Exists(Path.Combine(HsfLibrary.PackageOf(build), "reshade-shaders")));
    }

    [Fact]
    public void A_wrong_layout_is_refused_and_leaves_nothing_in_the_library()
    {
        Assert.Throws<InvalidDataException>(() => HsfLibrary.Import(Zip("HSF-0.9.4.zip", "a", skip: ["hsf.addon64"])));
        Assert.Throws<InvalidDataException>(() => HsfLibrary.Import(Folder("no-streamline", "a", skip: ["HSF_streamline/sl.interposer.dll"])));
        Assert.Throws<InvalidDataException>(() => HsfLibrary.Import(Path.Combine(_releases, "missing")));

        Assert.Empty(HsfLibrary.Builds());
        Assert.False(Directory.Exists(HsfLibrary.Root + ".new"));
    }

    [Fact]
    public void Remove_takes_the_build_out_of_the_library()
    {
        var build = HsfLibrary.Import(Zip("HSF-0.9.4.zip", "a"));

        HsfLibrary.Remove(build);

        Assert.Empty(HsfLibrary.Builds());
    }

    // ---------- install ----------

    private GameInfo Game(int bits, string? antiCheat = null)
    {
        var g = GameScanner.Inspect(new GameEntry("Test Game", _root, "Manual"));
        g.Tech.Bits = bits;
        g.Tech.Apis = GraphicsApis.Dx11;
        g.AntiCheat = antiCheat;
        return g;
    }

    private InstalledComponent? Record() => InstallManifest.Read(_target)?.Component(ComponentRegistry.Hsf);

    [Fact]
    public async Task A_64_bit_game_gets_the_64_bit_add_on_the_streamline_folder_and_the_shader_and_all_of_it_is_recorded()
    {
        var build = HsfLibrary.Import(Zip("HSF-0.9.4.zip", "a"));

        await _service.InstallAsync(Game(64), _target, new HdrReShade(), null, default);

        Assert.Equal(Content("a", "hsf.addon64"), File.ReadAllText(Path.Combine(_target, "hsf.addon64")));
        Assert.False(File.Exists(Path.Combine(_target, "hsf.addon32")));
        Assert.Equal(Content("a", "HSF_streamline/sl.interposer.dll"), File.ReadAllText(Path.Combine(_target, "HSF_streamline", "sl.interposer.dll")));
        Assert.True(File.Exists(Path.Combine(_target, "HSF_streamline", "hsf-dlssg-helper64.exe")));
        Assert.True(File.Exists(Path.Combine(_target, "reshade-shaders", "Shaders", "HSF.fx")));
        Assert.False(File.Exists(Path.Combine(_target, "README.txt")));
        var m = InstallManifest.Read(_target)!;
        Assert.True(m.ReShade);
        var c = Record()!;
        Assert.Equal(build.Id, c.BuildId);
        Assert.Equal("0.9.4", c.Version);
        Assert.Equal(9, c.Files.Count);
        Assert.Equal("64", c.Extras["bits"]);
        Assert.Contains("HSF", m.ModLabel());
    }

    [Fact]
    public async Task A_32_bit_game_gets_the_32_bit_add_on_and_still_the_streamline_folder()
    {
        HsfLibrary.Import(Zip("HSF-0.9.4.zip", "a"));

        await _service.InstallAsync(Game(32), _target, new HdrReShade(), null, default);

        Assert.True(File.Exists(Path.Combine(_target, "hsf.addon32")));
        Assert.False(File.Exists(Path.Combine(_target, "hsf.addon64")));
        Assert.True(File.Exists(Path.Combine(_target, "HSF_streamline", "sl.interposer.dll")));
        Assert.Equal("32", Record()!.Extras["bits"]);
    }

    [Fact]
    public async Task A_game_with_unknown_bitness_gets_the_64_bit_add_on_only_and_the_record_says_so()
    {
        HsfLibrary.Import(Zip("HSF-0.9.4.zip", "a"));

        await _service.InstallAsync(Game(0), _target, new HdrReShade(), null, default);

        Assert.True(File.Exists(Path.Combine(_target, "hsf.addon64")));
        Assert.False(File.Exists(Path.Combine(_target, "hsf.addon32")));
        Assert.Contains("unknown", Record()!.Extras["bits"]);
    }

    [Fact]
    public async Task The_add_on_goes_into_the_AddonPath_folder_and_the_streamline_folder_stays_beside_the_exe()
    {
        Write(Path.Combine(_target, "ReShade.ini"), "[ADDON]\nAddonPath=.\\addons\n");
        HsfLibrary.Import(Zip("HSF-0.9.4.zip", "a"));

        await _service.InstallAsync(Game(64), _target, new HdrReShade(), null, default);

        Assert.True(File.Exists(Path.Combine(_target, "addons", "hsf.addon64")));
        Assert.True(File.Exists(Path.Combine(_target, "HSF_streamline", "sl.interposer.dll")));
    }

    [Fact]
    public async Task Remove_takes_the_files_out_and_puts_back_what_the_install_replaced()
    {
        Write(Path.Combine(_target, "hsf.addon64"), "THE USERS OWN");
        HsfLibrary.Import(Zip("HSF-0.9.4.zip", "a"));
        var game = Game(64);
        await _service.InstallAsync(game, _target, new HdrReShade(), null, default);
        Assert.Equal(Content("a", "hsf.addon64"), File.ReadAllText(Path.Combine(_target, "hsf.addon64")));

        await _service.RemoveAsync(game, _target, default);

        Assert.Equal("THE USERS OWN", File.ReadAllText(Path.Combine(_target, "hsf.addon64")));
        Assert.False(Directory.Exists(Path.Combine(_target, "HSF_streamline")));
        Assert.False(File.Exists(Path.Combine(_target, "reshade-shaders", "Shaders", "HSF.fx")));
        Assert.Null(Record());
    }

    [Fact]
    public async Task A_game_with_anti_cheat_and_a_missing_import_are_refused_and_nothing_is_placed()
    {
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.InstallAsync(Game(64), _target, new HdrReShade(), null, default));
        Assert.Contains("No HSF release", ex1.Message);

        HsfLibrary.Import(Zip("HSF-0.9.4.zip", "a"));
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.InstallAsync(Game(64, "EasyAntiCheat"), _target, new HdrReShade(), null, default));
        Assert.Contains("anti-cheat", ex2.Message);
        Assert.False(File.Exists(Path.Combine(_target, "hsf.addon64")));
        Assert.Null(InstallManifest.Read(_target));
    }

    [Fact]
    public async Task ReShade_older_than_6_8_is_refused_with_the_READMEs_requirement()
    {
        UseReShade("6.7.1.2000");
        HsfLibrary.Import(Zip("HSF-0.9.4.zip", "a"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.InstallAsync(Game(64), _target, new HdrReShade(), null, default));

        Assert.Contains("6.8", ex.Message);
        Assert.False(File.Exists(Path.Combine(_target, "hsf.addon64")));
    }

    // ---------- advisor ----------

    private static readonly GpuInfo Rtx40 = new(GpuVendor.Nvidia, RtxTier.Rtx40, "GeForce RTX 4070");
    private static readonly GpuInfo Rtx30 = new(GpuVendor.Nvidia, RtxTier.Rtx30, "GeForce RTX 3060");

    private static AdvisorInput Advice(GraphicsApis api, GpuInfo gpu, bool imported = true, bool fg = false, bool dlss = false, string? antiCheat = null)
    {
        var g = new GameInfo { Name = "Test Game", Root = @"C:\Games\Test", AntiCheat = antiCheat, Exes = [@"C:\Games\Test\game.exe"] };
        g.Tech = new GameTech { Apis = api, Bits = 64 };
        if (fg) g.Dlss.Add(new DlssDll { Name = "nvngx_dlssg.dll", Path = @"C:\Games\Test\nvngx_dlssg.dll" });
        return new AdvisorInput(g, gpu) { NativeDlss = dlss, Imported = imported ? new HashSet<string> { ComponentRegistry.Hsf } : new HashSet<string>() };
    }

    [Fact]
    public void The_advisor_offers_HSF_only_once_a_release_is_imported_and_the_game_has_no_frame_generation()
    {
        Assert.DoesNotContain(MethodAdvisor.Advise(Advice(GraphicsApis.Dx11, Rtx40, imported: false)).Methods, m => m.Method == Method.Hsf);
        Assert.DoesNotContain(MethodAdvisor.Advise(Advice(GraphicsApis.Dx11, Rtx40, fg: true)).Methods, m => m.Method == Method.Hsf);

        var hsf = MethodAdvisor.Advise(Advice(GraphicsApis.Dx11, Rtx40)).Methods.Single(m => m.Method == Method.Hsf);

        Assert.Equal("HSF frame generation (preview)", hsf.Title);
        Assert.Equal(Availability.NeedsSetup, hsf.Availability);
        Assert.Empty(hsf.Components);
        Assert.Contains(hsf.Needs, n => n.Contains("GITC Uplift"));
        Assert.Contains(hsf.Needs, n => n.Contains("Borderless"));
        Assert.DoesNotContain(hsf.Needs, n => n.Contains("MFG Unlock"));
    }

    [Fact]
    public void A_DirectX_12_game_with_DLSS_needs_no_flow_source_and_an_RTX_30_is_pointed_to_MFG_Unlock()
    {
        var hsf = MethodAdvisor.Advise(Advice(GraphicsApis.Dx12, Rtx30, dlss: true)).Methods.Single(m => m.Method == Method.Hsf);

        Assert.DoesNotContain(hsf.Needs, n => n.Contains("flow source"));
        Assert.Contains(hsf.Needs, n => n.Contains("MFG Unlock"));
    }

    [Fact]
    public void An_anti_cheat_game_gets_the_existing_anti_cheat_caution_on_the_HSF_option()
    {
        var hsf = MethodAdvisor.Advise(Advice(GraphicsApis.Dx11, Rtx40, antiCheat: "EasyAntiCheat")).Methods.Single(m => m.Method == Method.Hsf);

        Assert.Equal(MethodAdvisor.AntiCheatCaution("EasyAntiCheat"), hsf.Warning);
    }

    [Fact]
    public void The_version_is_read_from_the_name()
    {
        Assert.Equal("0.9.4", HsfLibrary.VersionOf("HSF-0.9.4"));
        Assert.Equal("1.0", HsfLibrary.VersionOf("HSF 1.0 (preview)"));
        Assert.Null(HsfLibrary.VersionOf("release"));
    }
}
