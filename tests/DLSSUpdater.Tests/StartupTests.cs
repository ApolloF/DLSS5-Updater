using System.Text.Json.Nodes;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Startup safety: damaged or newer settings.json, and the one-instance lock.</summary>
public sealed class StartupTests : IDisposable
{
    public StartupTests()
    {
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "dlssu-start-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(AppPaths.Root, true); } catch (IOException) { }
    }

    [Fact]
    public void NullCollections_LoadAsDefaults()
    {
        File.WriteAllText(AppPaths.SettingsFile, """
            {"Games":null,"ManualGames":null,"LibraryRoots":[null,"D:\\Games"],"HiddenGames":null,
             "IniOverrides":null,"ReShadeOverrides":[null,{"Section":"GENERAL","Key":"PerformanceMode","Value":null}],
             "SeenReleases":null,"AnnouncedReleases":null,"AddonPairTests":null,"IniMode":null,"DefaultProxy":null,"OptiSource":null,
             "Presets":[null,{"Name":"Mine","Opti":null,"ReShade":null}]}
            """);

        var s = AppSettings.Load();

        Assert.Empty(s.Games);
        Assert.Empty(s.ManualGames);
        Assert.Equal([@"D:\Games"], s.LibraryRoots);
        Assert.Equal(new AppSettings().IniOverrides.Count, s.IniOverrides.Count);
        Assert.Equal("", Assert.Single(s.ReShadeOverrides).Value);
        var preset = Assert.Single(s.Presets);
        Assert.Empty(preset.Opti);
        Assert.Equal("keep", s.IniMode);
        Assert.Equal("dxgi.dll", s.DefaultProxy);
        Assert.Equal(ComponentStore.OptiRepo, s.OptiSource);
        s.For("C:\\Games\\X").Mode = InstallMode.ReShadeOnly;
        Assert.NotNull(s.For("c:\\games\\x").Mode);
        Assert.False(File.Exists(AppSettings.BadSettingsFile));
    }

    [Fact]
    public void NullPresets_LoadAsEmpty()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"Presets":null,"Games":{"c:\\g":null}}""");
        var s = AppSettings.Load();
        Assert.Empty(s.Presets);
        Assert.Empty(s.Games);
    }

    [Fact]
    public void GameIdsDifferingOnlyInCase_KeepTheFirst()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"Games":{"C:\\Games\\X":{"Preset":"First"},"c:\\games\\x":{"Preset":"Second"}}}""");
        var s = AppSettings.Load();
        Assert.Equal("First", Assert.Single(s.Games).Value.Preset);
        Assert.Equal("First", s.Games[@"c:\games\x"].Preset);
    }

    [Fact]
    public void UnreadableSettings_AreKeptBeforeDefaultsOverwriteThem()
    {
        const string broken = """{"ManualGames":["D:\\Games\\A"], oops""";
        File.WriteAllText(AppPaths.SettingsFile, broken);

        var s = AppSettings.Load();
        Assert.Empty(s.ManualGames);
        s.Save();

        Assert.Equal(broken, File.ReadAllText(AppSettings.BadSettingsFile));
        Assert.NotEqual(broken, File.ReadAllText(AppPaths.SettingsFile));
    }

    [Fact]
    public void FieldsFromANewerVersion_SurviveASave()
    {
        File.WriteAllText(AppPaths.SettingsFile, """
            {"IniMode":"apply","FutureOption":{"a":[1,2]},"Games":{"c:\\g":{"Preset":"Mine","FutureGameOption":true}}}
            """);

        var s = AppSettings.Load();
        s.IniMode = "fresh";
        s.Save();

        var json = JsonNode.Parse(File.ReadAllText(AppPaths.SettingsFile))!;
        Assert.Equal("fresh", (string?)json["IniMode"]);
        Assert.Equal(2, (int)json["FutureOption"]!["a"]![1]!);
        Assert.True((bool)json["Games"]!["c:\\g"]!["FutureGameOption"]!);
        Assert.Equal("Mine", (string?)json["Games"]!["c:\\g"]!["Preset"]);
        Assert.Null(json["Unknown"]);
    }

    [Fact]
    public void SecondInstance_IsRefused_UntilTheFirstEnds()
    {
        var name = @"Local\DLSSUpdater.Test." + Guid.NewGuid().ToString("N");
        // A mutex is re-entrant on its owning thread, so each "process" gets its own dedicated thread (pool threads
        // can be reused by the next step and would re-enter the lock).
        SingleInstance? first = null;
        using var acquired = new ManualResetEventSlim();
        using var end = new ManualResetEventSlim();
        var firstThread = new Thread(() =>
        {
            first = SingleInstance.TryAcquire(name, TimeSpan.Zero);
            acquired.Set();
            end.Wait();
        });
        firstThread.Start();
        acquired.Wait();
        Assert.NotNull(first);

        SingleInstance? second = null;
        OnOwnThread(() =>
        {
            second = SingleInstance.TryAcquire(name, TimeSpan.Zero);
            second?.Dispose();
        });
        Assert.Null(second);

        // The first instance ends: its thread exits without releasing, as a closing process does.
        end.Set();
        firstThread.Join();
        SingleInstance? third = null;
        OnOwnThread(() =>
        {
            third = SingleInstance.TryAcquire(name, TimeSpan.FromSeconds(5));
            third?.Dispose();
        });
        Assert.NotNull(third);
    }

    private static void OnOwnThread(Action action)
    {
        var thread = new Thread(() => action());
        thread.Start();
        thread.Join();
    }
}
