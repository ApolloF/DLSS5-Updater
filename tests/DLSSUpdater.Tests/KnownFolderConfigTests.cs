using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>A record naming a config file in AppData/Documents only changes it when it lies in the game's own Unreal config folder.</summary>
public sealed class KnownFolderConfigTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-kf-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root, _target, _local, _docs;
    private readonly Installer _installer;

    public KnownFolderConfigTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, @"Game\Binaries\Win64");
        _local = Path.Combine(_tmp, "LocalAppData");
        _docs = Path.Combine(_tmp, "Documents");
        InstallManifest.KnownFolder = tag => tag switch { "|localappdata|" => _local, "|documents|" => _docs, _ => null };
        Directory.CreateDirectory(_target);
        File.WriteAllText(Path.Combine(_target, "Game-Win64-Shipping.exe"), "EXE");
        File.WriteAllText(Path.Combine(_root, "Game.exe"), "BOOT");
        _installer = new Installer(new ComponentStore(new GitHubClient(), () => true));
    }

    public void Dispose()
    {
        InstallManifest.KnownFolder = InstallManifest.DefaultKnownFolder;
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    private GameInfo Game()
    {
        var g = GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));
        g.Tech.Engine = GameEngine.Unreal;
        g.Tech.UeProject = "Game";
        return g;
    }

    private string EngineIni => Path.Combine(_local, @"Game\Saved\Config\Windows\Engine.ini");

    private Task Install(string ini) => _installer.InstallComponentAsync(Game(), _target,
        new ComponentPlan(ComponentRegistry.UnrealFrameCap) { Version = "60", ConfigEdits = [(ini, "SystemSettings", "t.MaxFPS", "60")] }, default);

    [Fact]
    public async Task The_Unreal_config_folder_of_the_game_is_changed_and_restored()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EngineIni)!);
        File.WriteAllText(EngineIni, "[SystemSettings]\nt.MaxFPS=144\n");
        await Install(EngineIni);
        Assert.Equal("60", IniFile.Load(EngineIni).Get("SystemSettings", "t.MaxFPS"));

        await _installer.RemoveComponentAsync(Game(), _target, ComponentRegistry.UnrealFrameCap, default);

        Assert.Equal("144", IniFile.Load(EngineIni).Get("SystemSettings", "t.MaxFPS"));
    }

    [Fact]
    public async Task A_record_edited_to_name_another_apps_ini_leaves_that_file_alone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EngineIni)!);
        await Install(EngineIni);
        var other = Path.Combine(_local, @"OtherApp\settings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "[SystemSettings]\nt.MaxFPS=60\nkeep=1\n");
        var m = InstallManifest.Read(_target)!;
        m.Component(ComponentRegistry.UnrealFrameCap)!.ConfigEdits[0].File = "|localappdata|" + Path.DirectorySeparatorChar + @"OtherApp\settings.ini";
        m.Save(_target);

        await _installer.RemoveComponentAsync(Game(), _target, ComponentRegistry.UnrealFrameCap, default);

        var ini = IniFile.Load(other);
        Assert.Equal("60", ini.Get("SystemSettings", "t.MaxFPS"));
        Assert.Equal("1", ini.Get("SystemSettings", "keep"));
        Assert.Null(InstallManifest.Read(_target)?.Component(ComponentRegistry.UnrealFrameCap));
    }
}
