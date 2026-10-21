using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

public class ScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dlssu-scan-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Vdf_ParsesLibraryFolders()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"      "C:\\Program Files (x86)\\Steam"
                    "apps" { "228980" "1" }
                }
                "1" { "path" "R:\\SteamLibrary" }
            }
            """;
        var root = VdfNode.Parse(vdf).Child("libraryfolders")!;
        Assert.Equal(@"C:\Program Files (x86)\Steam", root.Child("0")!["path"]);
        Assert.Equal(@"R:\SteamLibrary", root.Child("1")!["path"]);
        Assert.Equal("1", root.Child("0")!.Child("apps")!["228980"]);
    }

    private string Touch(string rel, int size = 16)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
        return p;
    }

    [Fact]
    public void Inspect_PrefersUnrealShipping_FindsDlssAndAntiCheat()
    {
        Touch(@"MyGame.exe", 100);
        var shipping = Touch(@"MyGame\Binaries\Win64\MyGame-Win64-Shipping.exe", 50);
        Touch(@"Engine\Binaries\Win64\CrashReportClient.exe", 500);
        Touch(@"Engine\Plugins\Runtime\Nvidia\DLSS\Binaries\ThirdParty\Win64\nvngx_dlss.dll");
        Touch(@"MyGame\Binaries\Win64\nvngx_dlssg.dll");
        Touch(@"_CommonRedist\vcredist\vc_redist.x64.exe", 900);
        Directory.CreateDirectory(Path.Combine(_root, "EasyAntiCheat"));

        var g = new GameInfo { Root = _root, Name = "My Game" };
        GameInspector.Inspect(g);

        Assert.Equal(shipping, g.Exes[0]);
        Assert.Equal(2, g.Dlss.Count);
        Assert.Contains(g.Dlss, d => d.Kind == "FG");
        Assert.Equal("EasyAntiCheat", g.AntiCheat);
        Assert.DoesNotContain(g.Exes, e => e.Contains("vc_redist"));
    }

    [Fact]
    public void Inspect_UnityPlayerBeatsBiggerModTool()
    {
        var game = Touch(@"Beat Saber.exe", 10);
        Directory.CreateDirectory(Path.Combine(_root, "Beat Saber_Data"));
        Touch(@"ModAssistant.exe", 4_000_000);
        Touch(@"UnityCrashHandler64.exe", 2_000_000);

        var g = new GameInfo { Root = _root, Name = "Beat Saber" };
        GameInspector.Inspect(g);

        Assert.Equal(game, g.Exes[0]);
    }

    [Fact]
    public void Vdf_UnescapesQuotedStrings()
    {
        var node = VdfNode.Parse("""
            "name" "a \"quoted\" C:\\Games\tTab"
            """);
        Assert.Equal("a \"quoted\" C:\\Games\tTab", node["name"]);
    }

    [Fact]
    public void Vdf_SkipsComments_ReadsUnquotedTokens_IgnoresKeyCase()
    {
        const string vdf = """
            // written by Steam
            AppState
            {
                appid 1091500 // trailing comment
                "InstallDir" "Cyberpunk 2077"
            }
            """;
        var app = VdfNode.Parse(vdf).Child("appstate")!;
        Assert.Equal("1091500", app["APPID"]);
        Assert.Equal("Cyberpunk 2077", app["installdir"]);
        Assert.Null(app["missing"]);
    }

    [Theory]
    [InlineData("\"a\" { \"b\" \"c\"")]   // file cut off before the closing brace
    [InlineData("\"a\" { \"b\" \"c")]     // ... and inside a string
    public void Vdf_TruncatedFile_KeepsWhatWasRead(string vdf) =>
        Assert.Equal("c", VdfNode.Parse(vdf).Child("a")!["b"]);

    [Fact]
    public void Vdf_DanglingKey_IsIgnored() => Assert.Empty(VdfNode.Parse("\"a\"").Values);

    [Theory]
    [InlineData("nvngx_dlss.dll", "SR")]
    [InlineData("NVNGX_DLSSD.DLL", "RR")]
    [InlineData("nvngx_dlssg.dll", "FG")]
    [InlineData("sl.interposer.dll", "SL")]
    [InlineData("nvngx_dlssnr.dll", "?")]
    public void DlssDll_KindFromName(string name, string kind) => Assert.Equal(kind, new DlssDll { Name = name }.Kind);

    [Fact]
    public void FgVersion_IsNewestFrameGenDll()
    {
        var g = new GameInfo
        {
            Dlss =
            [
                new DlssDll { Name = "nvngx_dlss.dll", Version = new Version(310, 9, 1) },
                new DlssDll { Name = "nvngx_dlssg.dll", Version = new Version(3, 7, 10) },
                new DlssDll { Name = "nvngx_dlssg.dll", Version = new Version(310, 2, 0) },
            ],
        };
        Assert.Equal(new Version(310, 2, 0), g.FgVersion);
        Assert.Null(new GameInfo { Dlss = [new DlssDll { Name = "nvngx_dlss.dll", Version = new Version(1, 0) }] }.FgVersion);
    }

    [Fact]
    public void MakeId_IgnoresCaseAndTrailingSlash() =>
        Assert.Equal(GameInfo.MakeId(@"C:\Games\Foo\"), GameInfo.MakeId(@"c:\games\FOO"));

    [Fact]
    public void Normalize_KeepsTheSeparatorOfADriveRoot()
    {
        Assert.Equal(@"C:\", Core.FileUtil.Normalize(@"C:\"));
        Assert.Equal(@"C:\Games", Core.FileUtil.Normalize(@"C:\Games\"));
        Assert.Equal(@"c:\", GameInfo.MakeId(@"C:\"));
        Assert.Equal(@"C:\Games", Path.Combine(Core.FileUtil.Normalize(@"C:\"), "Games"));
    }

    private string SteamLibrary(string name, string game)
    {
        var lib = Path.Combine(_root, name);
        var apps = Path.Combine(lib, "steamapps");
        Directory.CreateDirectory(Path.Combine(apps, "common", game));
        File.WriteAllText(Path.Combine(apps, "appmanifest_1.acf"), $$"""
            "AppState" { "appid" "1" "name" "{{game}}" "installdir" "{{game}}" }
            """);
        return lib;
    }

    [Fact]
    public void Steam_UnreadableLibrary_OnlyDropsItself()
    {
        var locked = SteamLibrary("locked", "Hidden Game");
        var open = SteamLibrary("open", "Good Game");
        var apps = new DirectoryInfo(Path.Combine(locked, "steamapps"));
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.ListDirectory, System.Security.AccessControl.AccessControlType.Deny);
        var acl = apps.GetAccessControl();
        acl.AddAccessRule(deny);
        apps.SetAccessControl(acl);
        try
        {
            var games = Launchers.SteamLibraries([locked, open]);
            Assert.Equal("Good Game", Assert.Single(games).Name);
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            apps.SetAccessControl(acl);
        }
    }

    [Fact]
    public void Steam_AppId_FromTheManifest_ReachesTheInspectedGame()
    {
        var lib = SteamLibrary("ids", "Known Game");
        Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common", "Copied Game"));
        File.WriteAllBytes(Path.Combine(lib, "steamapps", "common", "Copied Game", "game.exe"), [(byte)'M', (byte)'Z']);

        var games = Launchers.SteamLibraries([lib]).OrderBy(g => g.Name).ToList();

        Assert.Equal(["Copied Game", "Known Game"], games.Select(g => g.Name).ToArray());
        Assert.Null(games[0].SteamAppId);
        Assert.Equal(1, games[1].SteamAppId);
        Assert.Equal(1, GameScanner.Inspect(games[1]).SteamAppId);
        Assert.Null(GameScanner.Inspect(new GameEntry("Other", _root, "Manual")).SteamAppId);
    }

    [Fact]
    public void Epic_DamagedManifest_OnlyDropsItself()
    {
        var dir = Path.Combine(_root, "epic");
        var game = Path.Combine(_root, "EpicGame");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(game);
        var location = System.Text.Json.JsonSerializer.Serialize(game);
        File.WriteAllText(Path.Combine(dir, "a.item"), $$"""{"AppCategories":[1,{"x":2}],"DisplayName":5,"InstallLocation":{{location}}}""");
        File.WriteAllText(Path.Combine(dir, "b.item"), "[1,2]");
        File.WriteAllText(Path.Combine(dir, "c.item"), $$"""{"AppCategories":["games"],"DisplayName":"Epic Game","InstallLocation":{{location}}}""");
        File.WriteAllText(Path.Combine(dir, "d.item"), $$"""{"DisplayName":7,"InstallLocation":{{location}}}""");

        var games = Launchers.EpicManifests(dir);

        Assert.Equal(["Epic Game", "EpicGame"], games.Select(g => g.Name).ToArray());
    }

    private const string FakeRoot = @"X:\Games\Starfield";

    [Fact]
    public void RankExes_DropsInstallersCrashHandlersServersAndEngineTools()
    {
        var ranked = GameInspector.RankExes(FakeRoot,
        [
            (FakeRoot + @"\unins000.exe", 3_000_000),
            (FakeRoot + @"\UnityCrashHandler64.exe", 2_000_000),
            (FakeRoot + @"\_CommonRedist\vc_redist.x64.exe", 25_000_000),
            (FakeRoot + @"\StarfieldServer.exe", 90_000_000),
            (FakeRoot + @"\Engine\Binaries\Win64\CrashReportClient.exe", 30_000_000),
            (FakeRoot + @"\Engine\Binaries\Win64\Tool.exe", 30_000_000),
            (FakeRoot + @"\Starfield.exe", 80_000_000),
        ]);
        Assert.Equal([FakeRoot + @"\Starfield.exe"], ranked);
    }

    [Fact]
    public void RankExes_PrefersFolderNameOverBiggerLauncherAndTools()
    {
        var ranked = GameInspector.RankExes(FakeRoot,
        [
            (FakeRoot + @"\StarfieldLauncher.exe", 120_000_000),
            (FakeRoot + @"\ModTool.exe", 200_000_000),
            (FakeRoot + @"\Starfield.exe", 60_000_000),
        ]);
        Assert.Equal(FakeRoot + @"\Starfield.exe", ranked[0]);
        Assert.Equal(3, ranked.Count);
    }

    [Fact]
    public void RankExes_UsesGameNameWhenFolderDiffers()
    {
        const string root = @"X:\SteamLibrary\common\SF";
        var ranked = GameInspector.RankExes(root,
            [(root + @"\Other.exe", 60_000_000), (root + @"\Starfield.exe", 60_000_000)], "Starfield");
        Assert.Equal(root + @"\Starfield.exe", ranked[0]);
    }

    [Fact]
    public void RankExes_PrefersX64OverX86Build()
    {
        var ranked = GameInspector.RankExes(FakeRoot,
            [(FakeRoot + @"\x86\Game.exe", 60_000_000), (FakeRoot + @"\bin\x64\Game.exe", 60_000_000)]);
        Assert.Equal(FakeRoot + @"\bin\x64\Game.exe", ranked[0]);
    }
}
