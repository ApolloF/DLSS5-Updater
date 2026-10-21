using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>
/// A component registered once (a dummy, as a new mod would be) installs, updates and uninstalls through its generic
/// record: placed files with hashes, originals backed up and restored, config keys and attributes put back, and whatever
/// changed since is left alone and reported.
/// </summary>
public class ComponentRecordTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-comp-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly string _target;
    private readonly string _local;
    private readonly string _src;
    private readonly ComponentStore _store;
    private readonly Installer _installer;
    private readonly ComponentDefinition _dummy;

    public ComponentRecordTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, @"Game\Binaries\Win64");
        _local = Path.Combine(_tmp, "LocalAppData");
        _src = Path.Combine(_tmp, "build");
        InstallManifest.KnownFolder = tag => tag == "|localappdata|" ? _local : null;
        _store = new ComponentStore(new GitHubClient(), () => true);
        _installer = new Installer(_store);
        // The one registration a new component needs.
        _dummy = ComponentRegistry.Register(new ComponentDefinition("test-hdr-" + Guid.NewGuid().ToString("N")[..8], "Test HDR mod")
        {
            Library = LibraryKind.Uplift, Label = "TestHDR", OwnedFiles = ["test-hdr.addon64", @"reshade-shaders\Shaders\TestHdr*.fx"],
        });

        Write(Path.Combine(_target, "Game-Win64-Shipping.exe"), "EXE");
        Write(Path.Combine(_root, "Game.exe"), "BOOT");
        // The game's own file that the mod replaces, and the game's settings in %LocalAppData%.
        Write(Path.Combine(_target, "test-hdr.addon64"), "GAME-ORIGINAL");
        Write(Path.Combine(_target, "game.ini"), "[Video]\nHDR=0\n");
        Write(EngineIni, "[SystemSettings]\nr.HDR.EnableHDROutput=0\n");
        Write(Path.Combine(_src, "test-hdr.addon64"), "HDR-1");
        Write(Path.Combine(_src, "TestHdr.fx"), "FX-1");
        Write(Path.Combine(_src, "TestHdrLut.fx"), "LUT-1");
    }

    public void Dispose()
    {
        InstallManifest.KnownFolder = InstallManifest.DefaultKnownFolder;
        try
        {
            foreach (var f in Directory.EnumerateFiles(_tmp, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_tmp, true);
        }
        catch (IOException) { }
    }

    private string EngineIni => Path.Combine(_local, @"TestGame\Saved\Config\Windows\Engine.ini");
    private string Shaders => Path.Combine(_target, @"reshade-shaders\Shaders");

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private GameInfo Game() => GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));

    private Dictionary<string, string> Snapshot(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f), File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    private ComponentPlan Plan(string build = "b1", bool lut = true, bool hdrKey = true) => new(_dummy.Id)
    {
        BuildId = build, Version = build,
        Files =
        [
            (Path.Combine(_src, "test-hdr.addon64"), Path.Combine(_target, "test-hdr.addon64")),
            (Path.Combine(_src, "TestHdr.fx"), Path.Combine(Shaders, "TestHdr.fx")),
            .. lut ? new[] { (Path.Combine(_src, "TestHdrLut.fx"), Path.Combine(Shaders, "TestHdrLut.fx")) } : [],
        ],
        ConfigEdits =
        [
            (Path.Combine(_target, "game.ini"), "Video", "HDR", "1"),
            (Path.Combine(_target, "renodx.ini"), "renodx", "PeakNits", "1000"),
            .. hdrKey ? new[] { (EngineIni, "SystemSettings", "r.HDR.EnableHDROutput", "1"), (EngineIni, "SystemSettings", "r.HDR.Display.OutputDevice", "3") } : [],
        ],
        ReadOnly = hdrKey ? [EngineIni] : [],
        Extras = new Dictionary<string, string> { ["Peak"] = "1000" },
    };

    [Fact]
    public async Task Install_RecordsEveryStep()
    {
        await _installer.InstallComponentAsync(Game(), _target, Plan(), default);

        Assert.Equal("HDR-1", File.ReadAllText(Path.Combine(_target, "test-hdr.addon64")));
        Assert.Contains("HDR=1", File.ReadAllText(Path.Combine(_target, "game.ini")));
        Assert.Contains("PeakNits=1000", File.ReadAllText(Path.Combine(_target, "renodx.ini")));
        Assert.Contains("r.HDR.EnableHDROutput=1", File.ReadAllText(EngineIni));
        Assert.True(new FileInfo(EngineIni).IsReadOnly);

        var m = InstallManifest.Read(_target)!;
        var c = Assert.Single(m.Components);
        Assert.Equal(_dummy.Id, c.Id);
        Assert.Equal("b1", c.BuildId);
        Assert.Equal("1000", c.Extras["Peak"]);
        Assert.Equal(3, c.Files.Count);
        Assert.All(c.Files, f => Assert.Equal(FileUtil.Sha256(Path.Combine(_root, f.Path)), f.Sha256));
        // Its files and backups are in the record-level lists too, which is all 2.2.x reads.
        Assert.All(c.Files, f => Assert.Contains(f.Path, m.Files));
        var backup = Assert.Single(c.Backups);
        Assert.Equal(@"Game\Binaries\Win64\test-hdr.addon64", backup.Key);
        Assert.Equal(backup.Value, Assert.Single(m.Backups).Backup);
        Assert.Equal("GAME-ORIGINAL", File.ReadAllText(Path.Combine(InstallManifest.BackupDirFor(_target), backup.Value)));

        Assert.Equal("0", c.EditOf(@"Game\Binaries\Win64\game.ini", "Video", "HDR")!.Previous);
        var peak = c.EditOf(@"Game\Binaries\Win64\renodx.ini", "renodx", "PeakNits")!;
        Assert.Null(peak.Previous);
        Assert.True(peak.FileCreated);
        var engine = c.EditOf(@"|localappdata|\TestGame\Saved\Config\Windows\Engine.ini", "SystemSettings", "r.HDR.Display.OutputDevice")!;
        Assert.Null(engine.Previous);
        Assert.False(engine.FileCreated);
        var attr = Assert.Single(c.Attributes);
        Assert.Equal(FileAttributes.ReadOnly, attr.After & FileAttributes.ReadOnly);
        Assert.Equal((FileAttributes)0, attr.Before & FileAttributes.ReadOnly);
        Assert.Equal("TestHDR", m.ModLabel());
    }

    [Fact]
    public async Task Uninstall_RestoresEverything()
    {
        var game = Snapshot(_root);
        var local = Snapshot(_local);

        await _installer.InstallComponentAsync(Game(), _target, Plan(), default);
        await _installer.UninstallAsync(Game(), _target, default);

        Assert.Equal(game, Snapshot(_root));
        Assert.Equal(local, Snapshot(_local));
        Assert.False(new FileInfo(EngineIni).IsReadOnly);
        Assert.False(Directory.Exists(InstallManifest.DirFor(_target)));
        Assert.False(Directory.Exists(Path.Combine(_target, "reshade-shaders")));
    }

    [Fact]
    public async Task RemoveComponent_UndoesOnlyIt_AndKeepsAReadOnlyFileTheUserLocked()
    {
        File.SetAttributes(EngineIni, FileAttributes.ReadOnly);
        var local = Snapshot(_local);
        await _installer.InstallComponentAsync(Game(), _target, Plan(), default);
        // Already read-only: nothing to record for it.
        Assert.Empty(InstallManifest.Read(_target)!.Components[0].Attributes);

        var undo = await _installer.RemoveComponentAsync(Game(), _target, _dummy.Id, default);

        Assert.True(undo.Clean);
        Assert.Equal(local, Snapshot(_local));
        Assert.True(new FileInfo(EngineIni).IsReadOnly);
        Assert.Equal("GAME-ORIGINAL", File.ReadAllText(Path.Combine(_target, "test-hdr.addon64")));
        Assert.False(File.Exists(Path.Combine(_target, "renodx.ini")));
        Assert.Null(InstallManifest.Read(_target));
    }

    [Fact]
    public async Task ChangedSince_IsLeftInPlaceAndReported()
    {
        await _installer.InstallComponentAsync(Game(), _target, Plan(), default);
        // The user swaps in their own build, edits a key, and unlocks Engine.ini.
        File.WriteAllText(Path.Combine(_target, "test-hdr.addon64"), "USER-BUILD");
        File.WriteAllText(Path.Combine(_target, "game.ini"), "[Video]\nHDR=2\n");
        File.SetAttributes(EngineIni, FileAttributes.Normal);

        var undo = await _installer.RemoveComponentAsync(Game(), _target, _dummy.Id, default);

        Assert.Equal([@"Game\Binaries\Win64\test-hdr.addon64"], undo.ChangedFiles);
        Assert.Equal([@"Game\Binaries\Win64\game.ini [Video] HDR"], undo.ChangedKeys);
        Assert.Equal([@"|localappdata|\TestGame\Saved\Config\Windows\Engine.ini"], undo.ChangedAttributes);
        Assert.Equal("USER-BUILD", File.ReadAllText(Path.Combine(_target, "test-hdr.addon64")));
        Assert.Equal("[Video]\nHDR=2\n", File.ReadAllText(Path.Combine(_target, "game.ini")));
        // What didn't change is undone all the same.
        Assert.False(File.Exists(Path.Combine(Shaders, "TestHdr.fx")));
        Assert.Equal("[SystemSettings]\nr.HDR.EnableHDROutput=0\n", File.ReadAllText(EngineIni));
        Assert.Null(InstallManifest.Read(_target));
    }

    [Fact]
    public async Task Update_DropsWhatTheNewBuildLacks_AndKeepsAChangedFileAsPrevious()
    {
        await _installer.InstallComponentAsync(Game(), _target, Plan(), default);
        File.WriteAllText(Path.Combine(_target, "test-hdr.addon64"), "USER-BUILD");
        Write(Path.Combine(_src, "test-hdr.addon64"), "HDR-2");

        await _installer.InstallComponentAsync(Game(), _target, Plan("b2", lut: false, hdrKey: false), default);

        Assert.Equal("HDR-2", File.ReadAllText(Path.Combine(_target, "test-hdr.addon64")));
        Assert.Equal("USER-BUILD", File.ReadAllText(Path.Combine(InstallManifest.DirFor(_target), "previous", "test-hdr.addon64")));
        Assert.False(File.Exists(Path.Combine(Shaders, "TestHdrLut.fx")));
        Assert.Equal("[SystemSettings]\nr.HDR.EnableHDROutput=0\n", File.ReadAllText(EngineIni));
        Assert.False(new FileInfo(EngineIni).IsReadOnly);
        var c = InstallManifest.Read(_target)!.Components.Single();
        Assert.Equal("b2", c.BuildId);
        Assert.Equal(2, c.Files.Count);
        Assert.Empty(c.Attributes);
        Assert.Equal(2, c.ConfigEdits.Count);
        // The original from before the first install is still the one kept.
        Assert.Equal("GAME-ORIGINAL", File.ReadAllText(Path.Combine(InstallManifest.BackupDirFor(_target), c.Backups.Single().Value)));

        await _installer.UninstallAsync(Game(), _target, default);
        Assert.Equal("GAME-ORIGINAL", File.ReadAllText(Path.Combine(_target, "test-hdr.addon64")));
    }

    [Fact]
    public async Task ReinstallingTheSameBuild_ChangesNothing()
    {
        await _installer.InstallComponentAsync(Game(), _target, Plan(), default);
        var before = Snapshot(_root);
        var record = InstallManifest.Read(_target)!.Components.Single();

        await _installer.InstallComponentAsync(Game(), _target, Plan(), default);

        var after = Snapshot(_root);
        before.Remove(@"Game\Binaries\Win64\.dlssupdater\manifest.json");
        after.Remove(@"Game\Binaries\Win64\.dlssupdater\manifest.json");
        Assert.Equal(before, after);
        var again = InstallManifest.Read(_target)!.Components.Single();
        Assert.Equal(record.ConfigEdits.Select(e => (e.Key, e.Previous)), again.ConfigEdits.Select(e => (e.Key, e.Previous)));
        Assert.Equal(record.Attributes.Single().Before, again.Attributes.Single().Before);
    }

    [Fact]
    public async Task Plan_ForAnUnregisteredOrLegacyComponent_IsRefused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _installer.InstallComponentAsync(Game(), _target, new ComponentPlan("nobody-registered-this"), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _installer.InstallComponentAsync(Game(), _target, new ComponentPlan(ComponentRegistry.ReShade), default));
        Assert.Null(InstallManifest.Read(_target));
    }

    [Fact]
    public async Task ConfigOutsideGameAndKnownFolders_IsRefused()
    {
        var outside = Path.Combine(_tmp, @"Elsewhere\x.ini");
        Write(outside, "[a]\nb=1\n");
        var plan = new ComponentPlan(_dummy.Id) { ConfigEdits = [(outside, "a", "b", "2")] };

        await Assert.ThrowsAsync<UnsafeManifestException>(() => _installer.InstallComponentAsync(Game(), _target, plan, default));
        Assert.Equal("[a]\nb=1\n", File.ReadAllText(outside));
    }

    [Fact]
    public async Task FileOfAnotherComponent_IsNotTakenOver()
    {
        var other = ComponentRegistry.Register(new ComponentDefinition("test-other-" + Guid.NewGuid().ToString("N")[..8], "Other"));
        await _installer.InstallComponentAsync(Game(), _target, Plan(), default);
        var plan = new ComponentPlan(other.Id) { Files = [(Path.Combine(_src, "TestHdr.fx"), Path.Combine(Shaders, "TestHdr.fx"))] };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _installer.InstallComponentAsync(Game(), _target, plan, default));
        Assert.Single(InstallManifest.Read(_target)!.Components);
    }

    [Fact]
    public async Task NextToALegacyInstall_UninstallRestoresBoth()
    {
        // An earlier install displaced a file the old way; the generic component then adds its own.
        Write(Path.Combine(_target, "dxgi.dll"), "OTHER-MOD");
        var before = Snapshot(_root);
        var m = new InstallManifest { RootRel = Path.GetRelativePath(_target, _root) };
        Directory.CreateDirectory(InstallManifest.BackupDirFor(_target));
        File.Move(Path.Combine(_target, "dxgi.dll"), Path.Combine(InstallManifest.BackupDirFor(_target), "dxgi.dll"));
        File.WriteAllText(Path.Combine(_target, "dxgi.dll"), "RESHADE");
        m.Files.Add(@"Game\Binaries\Win64\dxgi.dll");
        m.Backups.Add(new BackupEntry { Original = @"Game\Binaries\Win64\dxgi.dll", Backup = "dxgi.dll" });
        m.ReShade = true;
        m.Mode = InstallMode.ReShadeOnly;
        m.Proxy = "dxgi.dll";
        m.ReShadeSha = FileUtil.Sha256(Path.Combine(_target, "dxgi.dll"));
        m.Save(_target);

        await _installer.InstallComponentAsync(Game(), _target, Plan(hdrKey: false), default);
        var view = InstallManifest.Read(_target)!.InstalledComponents();
        Assert.Equal([_dummy.Id, ComponentRegistry.ReShade], view.Select(c => c.Id));
        Assert.DoesNotContain(view.Single(c => c.Legacy).Files, f => f.Path.Contains("test-hdr"));

        await _installer.UninstallAsync(Game(), _target, default);
        Assert.Equal(before, Snapshot(_root));
    }

    [Fact]
    public async Task LegacyFileChangedSince_UninstallLeavesIt()
    {
        // ReShade-only install whose dxgi.dll the user replaced with their own ReShade.
        Write(Path.Combine(_target, "dxgi.dll"), "MY-RESHADE");
        var m = new InstallManifest { RootRel = Path.GetRelativePath(_target, _root), ReShade = true, Mode = InstallMode.ReShadeOnly, Proxy = "dxgi.dll", ReShadeSha = "0000" };
        m.Files.Add(@"Game\Binaries\Win64\dxgi.dll");
        m.Files.Add(@"Game\Binaries\Win64\renodx-mfgunlock.addon64");
        Write(Path.Combine(_target, "renodx-mfgunlock.addon64"), "MFG");
        m.Save(_target);

        await _installer.UninstallAsync(Game(), _target, default);

        Assert.Equal("MY-RESHADE", File.ReadAllText(Path.Combine(_target, "dxgi.dll")));
        Assert.False(File.Exists(Path.Combine(_target, "renodx-mfgunlock.addon64")));
        Assert.False(Directory.Exists(InstallManifest.DirFor(_target)));
    }

    [Fact]
    public async Task RestoreDlss_LeavesAComponentsDlssFile()
    {
        Write(Path.Combine(_src, "nvngx_dlssg.dll"), "FEEDER-FG");
        var plan = new ComponentPlan(_dummy.Id) { BuildId = "b1", Files = [(Path.Combine(_src, "nvngx_dlssg.dll"), Path.Combine(_target, "nvngx_dlssg.dll"))] };
        await _installer.InstallComponentAsync(Game(), _target, plan, default);

        await _installer.RestoreDlssAsync(Game(), _target, default);

        Assert.Equal("FEEDER-FG", File.ReadAllText(Path.Combine(_target, "nvngx_dlssg.dll")));
        Assert.Single(InstallManifest.Read(_target)!.Components);
    }

    [Fact]
    public async Task CleanupAndKnownInstalls_ProtectTheComponentsBuildsById()
    {
        await _installer.InstallComponentAsync(Game(), _target, Plan(), default);
        var s = new AppSettings();
        s.SetDefaultBuild(_dummy.Id, "b-default");
        s.For("game").SetBuildPick(_dummy.Id, "b-picked");
        var m = InstallManifest.Read(_target)!;
        m.Dir = _target;

        var reasons = _store.ProtectionReasons(LibraryKind.Uplift, s, [m], null).ToList();

        Assert.Contains(reasons, r => r.Id == "b-default" && r.Reason == "default");
        Assert.Contains(reasons, r => r.Id == "b-picked" && r.Reason == "picked for game");
        Assert.Contains(reasons, r => r.Id == "b1" && r.Reason.StartsWith("installed in "));

        var refs = InstallRefs.From(m);
        Assert.Equal("b1", refs.Components![_dummy.Id]);
        Assert.Contains(LibraryKind.Uplift, refs.Libraries());
        // Read again, the same install gives equal references, so a scan doesn't rewrite install-refs.json.
        Assert.Equal(refs, InstallRefs.From(InstallManifest.Read(_target)!));
        Assert.NotEqual(refs, refs with { Components = new() { [_dummy.Id] = "b2" } });
        var json = JsonSerializer.Serialize(new Dictionary<string, InstallRefs> { ["x"] = refs }, JsonCtx.Default.DictionaryStringInstallRefs);
        Assert.Equal(refs, JsonSerializer.Deserialize(json, JsonCtx.Default.DictionaryStringInstallRefs)!["x"]);
        // An unplugged drive: the remembered build is still protected.
        var remembered = refs.ToManifest(_target);
        Assert.Contains(_store.ProtectionReasons(LibraryKind.Uplift, new AppSettings(), [remembered], null), r => r.Id == "b1");
    }

    [Fact]
    public void Registry_RefusesAnotherDefinitionUnderATakenId()
    {
        Assert.Same(_dummy, ComponentRegistry.Register(_dummy with { }));
        Assert.Throws<InvalidOperationException>(() => ComponentRegistry.Register(new ComponentDefinition(_dummy.Id, "Something else")));
        Assert.Throws<ArgumentException>(() => ComponentRegistry.Register(new ComponentDefinition("has space", "x")));
        Assert.True(_dummy.OwnsFile(@"reshade-shaders\Shaders\TestHdrLut.fx"));
        Assert.False(_dummy.OwnsFile(@"reshade-shaders\Shaders\Other.fx"));
        Assert.All(new[] { ComponentRegistry.OptiScaler, ComponentRegistry.Dfc, ComponentRegistry.Dlss }, id => Assert.True(ComponentRegistry.Find(id)!.Legacy));
    }
}
