using System.IO.Compression;
using System.Text;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>
/// Lecram's 32-bit add-on and NR's host (v12.9 and newer): the library keeps them with the build, the install places them beside the
/// game's ReShade and takes them out again. Fixtures are modelled on the real v12.9.9 setup and the v13.4.0 hand-install zip (same entry
/// names, version resources naming each file); nothing is run.
/// </summary>
public sealed class LecramHostTests : IDisposable
{
    private const string V11File = ComponentStore.Lecram11File;
    private static readonly string HostExe = Path.Combine(ComponentStore.LecramHostDir, ComponentStore.LecramHostExe);
    private static readonly string HostReShade = Path.Combine(ComponentStore.LecramHostDir, ComponentStore.ReShadeFile);
    private static readonly string HostAddon = Path.Combine(ComponentStore.LecramHostDir, V11File);

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-lchost-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly ComponentStore _store = new(new GitHubClient(), () => true);
    private readonly NrVersion _nr;

    public LecramHostTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "game.exe"), "EXE");
        var reshade = _store.ImportReShade(ReShadeFixtures.Folder(Path.Combine(_tmp, "rs"), "a", "6.8.0.2155"));
        _store.SelectedReShade = () => reshade.Id;
        var nr = Path.Combine(_tmp, "nr.dll");
        FakeVersionDll.Write(nr, new Version(310, 9, 16, 0), ComponentStore.DlssNrFile);
        _nr = _store.ImportDlssNr(nr);
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

    // ---------- fixtures ----------

    private string Src(string name)
    {
        var path = Path.Combine(_tmp, "src-" + Guid.NewGuid().ToString("N")[..6], name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string SystemDll(int bits) => Path.Combine(
        bits == 32 ? Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) : Environment.SystemDirectory, "version.dll");

    /// <summary>A PE file whose version resource names it, like every file of the real set.</summary>
    private byte[] Pe(string original, Version version, string internalName, int bits = 64, string extra = "")
    {
        var path = Src(original);
        FakeVersionDll.Write(path, version, original, internalName, SystemDll(bits));
        return [.. File.ReadAllBytes(path), .. Encoding.ASCII.GetBytes(extra + Guid.NewGuid())];
    }

    /// <summary>dlss5-nr.addon64 of v12.9.9 / v13.4.0: its version resource and the strings the app tells v11-generation builds by.</summary>
    private byte[] Addon64(string label) =>
        Pe(V11File, VersionOf(label), "nr-addon", extra: $"\0RenoDX.DLSS5\0NR-SETTINGS\0NR-COMPAT\0{label}NR on\0{label}\0");

    private static Version VersionOf(string label)
    {
        var p = label.TrimStart('v').Split('.').Select(int.Parse).ToArray();
        return new Version(p[0], p[1], p[2], 3000);
    }

    private sealed record Set(byte[] Addon64, byte[] Addon32, byte[] Exe, byte[] ReShade);

    private Set NewSet(string label) => new(Addon64(label), Pe(ComponentStore.Lecram32File, VersionOf(label), "relay32", bits: 32),
        Pe(ComponentStore.LecramHostExe, VersionOf(label), "nr-host"), File.ReadAllBytes(ReShadeFixtures.Dll(Path.Combine(_tmp, "fixed-" + Guid.NewGuid().ToString("N")[..6]), 64, "6.8.0.1")));

    private static byte[] ZipOf(params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, data) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(data);
            }
        return ms.ToArray();
    }

    /// <summary>"DLSS5 NR Add-on v13.4.0.zip": both add-ons at the root, the host folder ready with its own copy of the 64-bit add-on.</summary>
    private string HandZip(Set set, string label = "v13.4.0")
    {
        var path = Src($"DLSS5 NR Add-on {label}.zip");
        File.WriteAllBytes(path, ZipOf(
            ("dlss5-nr-host/" + V11File, set.Addon64),
            ("dlss5-nr-host/nr-host64.exe", set.Exe),
            ("dlss5-nr-host/ReShade64.dll", set.ReShade),
            (V11File, set.Addon64),
            ("dlss5-nr.addon32", set.Addon32),
            ("INSTALL.txt", "install"u8.ToArray()),
            ("RELEASE-NOTES.md", "notes"u8.ToArray()),
            ("THIRD-PARTY-NOTICES.txt", "notices"u8.ToArray())));
        return path;
    }

    /// <summary>"DLSS5-NR-Setup-v12.9.9.exe": a PE with a ZIP appended; the host's parts at the setup's own paths, no dlss5-nr-host folder.</summary>
    private string Setup(Set set, string label = "v12.9.9")
    {
        var zip = ZipOf(
            ("package.json", Encoding.UTF8.GetBytes($"{{\"schema\":1,\"version\":\"{label}\",\"kind\":\"v11\"}}")),
            ("sources.json", "{}"u8.ToArray()),
            (V11File, set.Addon64),
            ("dlss5-nr.addon32", set.Addon32),
            ("nr-host64.exe", set.Exe),
            ("reshade/ReShade64.dll", set.ReShade),
            ("reshade/ReShade32.dll", "MZ reshade32"u8.ToArray()),
            ("studio/renodx-dlss5.addon64", "MZ studio"u8.ToArray()),
            ("README.txt", "readme"u8.ToArray()));
        var exe = new byte[0x800];
        exe[0] = (byte)'M';
        exe[1] = (byte)'Z';
        var path = Src($"DLSS5-NR-Setup-{label}.exe");
        File.WriteAllBytes(path, [.. exe, .. zip]);
        return path;
    }

    private AddonBuild Bare(string label)
    {
        var path = Src(V11File);
        File.WriteAllBytes(path, Addon64(label));
        return _store.ImportLecram(path);
    }

    private GameInfo Game(int bits, GraphicsApis api, bool dlss = false)
    {
        var g = new GameInfo { Id = _root, Root = _root, Name = "Host game", Exes = [Path.Combine(_root, "game.exe")] };
        g.Tech = new GameTech { Bits = bits, Apis = api };
        if (dlss) g.Dlss.Add(new DlssDll { Name = "nvngx_dlss.dll", Path = Path.Combine(_root, "nvngx_dlss.dll") });
        return g;
    }

    private InstallOptions Lec(string? id, string proxy = "dxgi.dll", bool lecram = true) => new()
    {
        Mode = InstallMode.ReShadeOnly, ReShade = true, Lecram = lecram, DlssNr = lecram, DlssNrId = _nr.Id, Proxy = proxy, LecramId = id,
    };

    private string G(string rel) => Path.Combine(_root, rel);

    private Dictionary<string, string> Snapshot() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(_root, f), FileUtil.Sha256);

    private static string Lib(AddonBuild build, string rel) => Path.Combine(ComponentStore.LecramHostOf(build)!, rel);

    // ---------- library ----------

    [Fact]
    public void Import_HandInstallZip_KeepsTheHostSetWithTheBuild()
    {
        var set = NewSet("v13.4.0");

        var build = _store.ImportLecram(HandZip(set));

        Assert.Equal(("v13.4.0", Hash(set.Addon64)), (build.DetectedLabel, build.Id));
        Assert.True(ComponentStore.LecramHasHost(build));
        Assert.Equal(set.Addon32, File.ReadAllBytes(Lib(build, ComponentStore.Lecram32File)));
        Assert.Equal(set.Exe, File.ReadAllBytes(Lib(build, HostExe)));
        Assert.Equal(set.ReShade, File.ReadAllBytes(Lib(build, HostReShade)));
        Assert.Equal(set.Addon64, File.ReadAllBytes(Lib(build, HostAddon)));
        Assert.StartsWith("with NR's host", AddonLibraryViewModel.LecramHostInfo(build));
        // Nothing else of the zip goes into the library, and the import's staging folder is gone.
        var dir = Path.GetDirectoryName(ComponentStore.AddonPathOf(AddonKind.Lecram, build))!;
        Assert.Equal(["dlss5-nr-host\\dlss5-nr.addon64", "dlss5-nr-host\\nr-host64.exe", "dlss5-nr-host\\ReShade64.dll", "dlss5-nr.addon32", "dlss5-nr.addon64", "version.json"],
            Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir, f)).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Empty(Directory.GetDirectories(AppPaths.Cache, "lecram-import-*"));
    }

    private static string Hash(byte[] data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    private string WriteTemp(byte[] data)
    {
        var path = Src("hash.bin");
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public void Import_SetupExe_AssemblesTheHostFolderFromTheSetupsOwnPaths()
    {
        var set = NewSet("v12.9.9");
        var setup = Setup(set);
        var before = FileUtil.Sha256(setup);

        var build = _store.ImportLecram(setup);

        Assert.Equal("v12.9.9", build.DetectedLabel);
        Assert.Equal(set.Addon32, File.ReadAllBytes(Lib(build, ComponentStore.Lecram32File)));
        Assert.Equal(set.Exe, File.ReadAllBytes(Lib(build, HostExe)));
        Assert.Equal(set.ReShade, File.ReadAllBytes(Lib(build, HostReShade)));
        // The setup has no host folder: its root add-on is the host's (the hand-install zip's copy is the same file).
        Assert.Equal(set.Addon64, File.ReadAllBytes(Lib(build, HostAddon)));
        Assert.Equal(before, FileUtil.Sha256(setup));
    }

    [Fact]
    public void BareImport_HasNoHost_APackageAddsIt_AndABareImportAgainKeepsIt()
    {
        var set = NewSet("v13.4.0");
        var bare = WriteTemp(set.Addon64);
        var renamed = Path.Combine(Path.GetDirectoryName(bare)!, V11File);
        File.Move(bare, renamed);

        var first = _store.ImportLecram(renamed);
        Assert.Null(ComponentStore.LecramHostOf(first));
        Assert.StartsWith("64-bit add-on only", AddonLibraryViewModel.LecramHostInfo(first));

        var second = _store.ImportLecram(HandZip(set));
        Assert.Equal(first.Id, second.Id);
        Assert.Single(_store.LecramBuilds);
        Assert.True(ComponentStore.LecramHasHost(second));

        _store.ImportLecram(renamed);
        Assert.True(ComponentStore.LecramHasHost(_store.LecramBuildFor(first.Id)!));
    }

    [Fact]
    public void Import_RefusesHalfAHost_AndAHostFileThatNamesAnotherFile_ButTakesAnOlderSetupWithoutAHost()
    {
        var set = NewSet("v12.9.9");
        var half = Src("DLSS5 NR Add-on v12.9.9.zip");
        File.WriteAllBytes(half, ZipOf((V11File, set.Addon64), ("dlss5-nr.addon32", set.Addon32)));
        Assert.Contains("only in part", Assert.Throws<InvalidDataException>(() => _store.ImportLecram(half)).Message);

        var noReShade = Src("DLSS5 NR Add-on v12.9.9.zip");
        File.WriteAllBytes(noReShade, ZipOf((V11File, set.Addon64), ("dlss5-nr.addon32", set.Addon32),
            ("dlss5-nr-host/nr-host64.exe", set.Exe), ("dlss5-nr-host/" + V11File, set.Addon64)));
        Assert.Throws<InvalidDataException>(() => _store.ImportLecram(noReShade));

        var wrong = Src("DLSS5 NR Add-on v12.9.9.zip");
        File.WriteAllBytes(wrong, ZipOf((V11File, set.Addon64), ("dlss5-nr.addon32", set.Addon32),
            ("dlss5-nr-host/nr-host64.exe", Pe("something-else.exe", new Version(1, 0), "x")), ("dlss5-nr-host/ReShade64.dll", set.ReShade),
            ("dlss5-nr-host/" + V11File, set.Addon64)));
        Assert.Contains("something-else.exe", Assert.Throws<InvalidDataException>(() => _store.ImportLecram(wrong)).Message);
        Assert.Empty(_store.LecramBuilds);

        // v12.0.0-style setup: the author's ReShade may be in it, but no 32-bit add-on and no host exe, so no host.
        var older = Src("DLSS5-NR-Setup-v12.0.0.exe");
        var exe = new byte[0x800];
        exe[0] = (byte)'M';
        exe[1] = (byte)'Z';
        File.WriteAllBytes(older, [.. exe, .. ZipOf((V11File, Addon64("v12.0.0")), ("reshade/ReShade64.dll", set.ReShade))]);
        var build = _store.ImportLecram(older);
        Assert.Null(ComponentStore.LecramHostOf(build));
        Assert.Empty(Directory.GetDirectories(AppPaths.Cache, "lecram-import-*"));
    }

    [Fact]
    public void ABuildMissingPartOfItsHost_IsAnErrorAtInstall()
    {
        var build = _store.ImportLecram(HandZip(NewSet("v13.4.0")));
        File.Delete(Lib(build, HostExe));
        Assert.Throws<InvalidDataException>(() => ComponentStore.LecramHostOf(build));
        Assert.False(ComponentStore.LecramHasHost(build));
    }

    // ---------- install ----------

    [Fact]
    public async Task Install_In32BitDx9Game_PlacesBothAddonsAndTheHost_AndUninstallLeavesTheGameAsItWas()
    {
        var set = NewSet("v13.4.0");
        var build = _store.ImportLecram(HandZip(set));
        var game = Game(32, GraphicsApis.Dx9);
        var before = Snapshot();
        var installer = new Installer(_store);

        await installer.InstallAsync(game, _root, Lec(build.Id, "d3d9.dll"), null, default);

        Assert.True(Installer.IsReShade(G("d3d9.dll")));
        Assert.Equal(set.Addon64, File.ReadAllBytes(G(V11File)));
        Assert.Equal(set.Addon32, File.ReadAllBytes(G(ComponentStore.Lecram32File)));
        Assert.Equal(set.Exe, File.ReadAllBytes(G(HostExe)));
        Assert.Equal(set.ReShade, File.ReadAllBytes(G(HostReShade)));
        Assert.Equal(set.Addon64, File.ReadAllBytes(G(HostAddon)));
        var m = InstallManifest.Read(_root)!;
        Assert.Contains(ComponentStore.LecramHostDir, m.Dirs);
        foreach (var rel in ComponentStore.LecramHostFiles) Assert.Contains(rel, m.Files);
        var lecram = m.InstalledComponents().Single(c => c.Id == ComponentRegistry.Lecram);
        string[] expected = [V11File, .. ComponentStore.LecramHostFiles];
        Assert.Equal(expected.Order(StringComparer.OrdinalIgnoreCase),
            lecram.Files.Select(f => f.Path).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal([NrAddon.Lecram], NrAddons.PresentIn(_root, null).Where(r => r == NrAddon.Lecram));

        // NR's host writes its own ReShade.ini and log; they go with the folder the app created.
        File.WriteAllText(G(Path.Combine(ComponentStore.LecramHostDir, "ReShade.ini")), "[INPUT]\nKeyOverlay=36,0,0,0\n");
        File.WriteAllText(G(Path.Combine(ComponentStore.LecramHostDir, "ReShade.log")), "log");
        await installer.UninstallAsync(game, _root, default);

        Assert.False(Directory.Exists(G(ComponentStore.LecramHostDir)));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Install_OverAHandInstalledHost_BacksItUp_AndUninstallPutsItBack()
    {
        var old = NewSet("v12.9.9");
        Directory.CreateDirectory(G(ComponentStore.LecramHostDir));
        File.WriteAllBytes(G(ComponentStore.Lecram32File), old.Addon32);
        File.WriteAllBytes(G(HostExe), old.Exe);
        File.WriteAllBytes(G(HostReShade), old.ReShade);
        File.WriteAllBytes(G(HostAddon), old.Addon64);
        File.WriteAllText(G(Path.Combine(ComponentStore.LecramHostDir, "ReShade.ini")), "[INPUT]\nKeyOverlay=35,0,0,0\n");
        var before = Snapshot();
        var set = NewSet("v13.4.0");
        var build = _store.ImportLecram(HandZip(set));
        var game = Game(32, GraphicsApis.OpenGL);
        var installer = new Installer(_store);

        await installer.InstallAsync(game, _root, Lec(build.Id, "opengl32.dll"), null, default);

        Assert.Equal(set.Exe, File.ReadAllBytes(G(HostExe)));
        Assert.Equal(set.Addon32, File.ReadAllBytes(G(ComponentStore.Lecram32File)));
        var m = InstallManifest.Read(_root)!;
        Assert.DoesNotContain(ComponentStore.LecramHostDir, m.Dirs);
        foreach (var rel in ComponentStore.LecramHostFiles) Assert.NotNull(m.BackupOf(rel));

        await installer.UninstallAsync(game, _root, default);

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Switching_ToABuildWithoutHost_TakesTheHostOut_AndBackPutsItIn()
    {
        var host = _store.ImportLecram(HandZip(NewSet("v13.4.0")));
        var bare = Bare("v12.0.0");
        var game = Game(64, GraphicsApis.Dx11, dlss: true);
        var before = Snapshot();
        var installer = new Installer(_store);

        await installer.InstallAsync(game, _root, Lec(host.Id), null, default);
        Assert.True(File.Exists(G(HostExe)));

        await installer.InstallAsync(game, _root, Lec(bare.Id), null, default);
        Assert.Equal(FileUtil.Sha256(ComponentStore.AddonPathOf(AddonKind.Lecram, bare)), FileUtil.Sha256(G(V11File)));
        Assert.False(File.Exists(G(ComponentStore.Lecram32File)));
        Assert.False(Directory.Exists(G(ComponentStore.LecramHostDir)));
        var m = InstallManifest.Read(_root)!;
        Assert.Empty(m.Dirs);
        Assert.DoesNotContain(m.Files, f => ComponentStore.LecramHostFiles.Contains(f, StringComparer.OrdinalIgnoreCase));

        await installer.InstallAsync(game, _root, Lec(host.Id), null, default);
        Assert.True(File.Exists(G(HostExe)));
        Assert.True(File.Exists(G(ComponentStore.Lecram32File)));

        // A run without Lecram takes the host out with the add-on.
        await installer.InstallAsync(game, _root, Lec(null, lecram: false), null, default);
        Assert.False(File.Exists(G(V11File)));
        Assert.False(Directory.Exists(G(ComponentStore.LecramHostDir)));

        await installer.UninstallAsync(game, _root, default);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Install_RefusesWhatTheBuildCantRun()
    {
        var host = _store.ImportLecram(HandZip(NewSet("v13.4.0")));
        var bare = Bare("v12.9.9");
        var v119 = Bare("v11.9.0");
        var installer = new Installer(_store);

        // A 32-bit game runs NR through the host, which a bare add-on import lacks.
        var noHost = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(Game(32, GraphicsApis.Dx11), _root, Lec(bare.Id), null, default));
        Assert.Contains("host", noHost.Message);
        Assert.Contains("v12.9.9 or newer", noHost.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(Game(64, GraphicsApis.Dx10), _root, Lec(bare.Id), null, default));
        // Not supported by the author yet.
        var vulkan32 = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(Game(32, GraphicsApis.Vulkan), _root, Lec(host.Id), null, default));
        Assert.Contains("32-bit Vulkan", vulkan32.Message);
        // v11.9 needs the game's DLSS; v12 and newer run without it.
        var dlss = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(Game(64, GraphicsApis.Dx11), _root, Lec(v119.Id), null, default));
        Assert.Contains("native DLSS", dlss.Message);
        Assert.Null(InstallManifest.Read(_root));

        await installer.InstallAsync(Game(64, GraphicsApis.Dx12), _root, Lec(bare.Id), null, default);
        Assert.True(File.Exists(G(V11File)));
        Assert.False(File.Exists(G(ComponentStore.Lecram32File)));
    }

    [Fact]
    public async Task Install_ABuildWithTheHost_NeedsReShade68()
    {
        var old = _store.ImportReShade(ReShadeFixtures.Folder(Path.Combine(_tmp, "rs"), "old", "6.7.3.0"));
        var host = _store.ImportLecram(HandZip(NewSet("v13.4.0")));
        var installer = new Installer(_store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.InstallAsync(Game(32, GraphicsApis.Dx9), _root, Lec(host.Id, "d3d9.dll") with { ReShadeBuildPick = old.Id }, null, default));
        Assert.Contains("6.8", ex.Message);
    }
}
