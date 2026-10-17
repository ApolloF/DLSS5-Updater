using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

public class GameConfigTests
{
    private const string Game = "game-a";

    private static IniOverride O(string section, string key, string value) => new(section, key, value);
    private static IniOverride Unset(string section, string key) => new(section, key, "") { Unset = true };

    private static string[] Flat(IEnumerable<IniOverride> list) => list.Select(o => $"{o.Id}={o.Value}").Order().ToArray();

    private static AppSettings Settings()
    {
        var s = new AppSettings
        {
            IniOverrides = [O("DlssNr", "Intensity", "1.5"), O("DlssNr", "Enabled", "true")],
            ReShadeOverrides = [O("GENERAL", "PerformanceMode", "1")],
        };
        s.Presets.Add(new ConfigPreset { Name = "Global", Opti = [O("DlssNr", "Style", "1")], ReShade = [O("OVERLAY", "ShowFPS", "1")] });
        return s;
    }

    // ---------- Layer ----------

    [Fact]
    public void Layer_ValueOverridesTheBase_OtherKeysPassThrough()
    {
        var result = ConfigProfile.Layer([O("A", "x", "1"), O("A", "y", "2")], [O("A", "x", "9"), O("A", "z", "3")]);
        Assert.Equal(["A/x=9", "A/y=2", "A/z=3"], Flat(result));
    }

    [Fact]
    public void Layer_UnsetRemovesTheBaseKey_AndIsNeverInTheResult()
    {
        var result = ConfigProfile.Layer([O("A", "x", "1"), O("A", "y", "2")], [Unset("A", "x"), Unset("A", "not-in-base")]);
        Assert.Equal(["A/y=2"], Flat(result));
        Assert.DoesNotContain(result, o => o.Unset);
    }

    [Fact]
    public void Layer_NullOrEmpty_InheritsEverything()
    {
        IniOverride[] baseList = [O("A", "x", "1")];
        Assert.Equal(["A/x=1"], Flat(ConfigProfile.Layer(baseList, null)));
        Assert.Equal(["A/x=1"], Flat(ConfigProfile.Layer(baseList, [])));
    }

    [Fact]
    public void Layer_IdsCompareCaseInsensitively_AndIgnoreSurroundingSpace()
    {
        var result = ConfigProfile.Layer([O("DlssNr", "Intensity", "1.5"), O("DlssNr", "Style", "1")], [O("dlssnr", "INTENSITY", "0.5"), Unset(" DLSSNR ", " style ")]);
        Assert.Equal(["dlssnr/INTENSITY=0.5"], Flat(result));
    }

    // ---------- effective profile ----------

    [Fact]
    public void Effective_WithoutPreset_UsesTheCurrentSettings()
    {
        var s = Settings();
        Assert.Equal(Flat(s.IniOverrides), Flat(GameConfig.EffectiveOpti(s, Game)));
        Assert.Equal(Flat(s.ReShadeOverrides), Flat(GameConfig.EffectiveReShade(s, Game)));
    }

    [Fact]
    public void Effective_GlobalPresetIsTheBase_NotTheCurrentSettings()
    {
        var s = Settings();
        s.For(Game).Preset = "Global";
        Assert.Equal(["DlssNr/Style=1"], Flat(GameConfig.EffectiveOpti(s, Game)));
        Assert.Equal(["OVERLAY/ShowFPS=1"], Flat(GameConfig.EffectiveReShade(s, Game)));
    }

    [Fact]
    public void Effective_BuiltInPresetCanBeTheBase()
    {
        var s = Settings();
        s.For(Game).Preset = ConfigProfile.RecommendedName;
        Assert.Equal(Flat(ConfigProfile.Recommended()), Flat(GameConfig.EffectiveOpti(s, Game)));
    }

    [Fact]
    public void Effective_LocalPresetIsTheBase_AndOnlyForItsGame()
    {
        var s = Settings();
        s.For(Game).Presets = [new ConfigPreset { Name = "Mine", Opti = [O("DlssNr", "Style", "2")] }];
        s.For(Game).Preset = "Mine";
        s.For("game-b").Preset = "Mine";
        Assert.Equal(["DlssNr/Style=2"], Flat(GameConfig.EffectiveOpti(s, Game)));
        Assert.Equal(Flat(s.IniOverrides), Flat(GameConfig.EffectiveOpti(s, "game-b")));
        Assert.Null(s.PresetFor("game-b"));
    }

    [Fact]
    public void Effective_LayerGoesOnTopOfEachKindOfBase()
    {
        var s = Settings();
        var g = s.For(Game);
        g.Opti = [O("DlssNr", "Intensity", "0.5"), Unset("DlssNr", "Enabled")];
        g.ReShade = [O("OVERLAY", "ShowFPS", "1")];

        Assert.Equal(["DlssNr/Intensity=0.5"], Flat(GameConfig.EffectiveOpti(s, Game)));
        Assert.Equal(["GENERAL/PerformanceMode=1", "OVERLAY/ShowFPS=1"], Flat(GameConfig.EffectiveReShade(s, Game)));

        g.Preset = "Global";
        Assert.Equal(["DlssNr/Intensity=0.5", "DlssNr/Style=1"], Flat(GameConfig.EffectiveOpti(s, Game)));
        Assert.Equal(["OVERLAY/ShowFPS=1"], Flat(GameConfig.EffectiveReShade(s, Game)));

        g.Presets = [new ConfigPreset { Name = "Mine", Opti = [O("DlssNr", "Enabled", "false"), O("DlssNr", "Style", "2")] }];
        g.Preset = "Mine";
        Assert.Equal(["DlssNr/Intensity=0.5", "DlssNr/Style=2"], Flat(GameConfig.EffectiveOpti(s, Game)));
    }

    [Fact]
    public void Effective_DoesNotChangeTheBaseLists()
    {
        var s = Settings();
        s.For(Game).Opti = [Unset("DlssNr", "Enabled"), O("DlssNr", "Intensity", "0.5")];
        GameConfig.EffectiveOpti(s, Game);
        Assert.Equal(["DlssNr/Enabled=true", "DlssNr/Intensity=1.5"], Flat(s.IniOverrides));
    }

    // ---------- layer entries ----------

    [Fact]
    public void SetLayer_NullValue_UnsetsOnlyWhenTheBaseSetsTheKey()
    {
        var g = new GameOverride();
        GameConfig.SetLayer(g, false, "DlssNr", "Enabled", null, baseSetsKey: false);
        Assert.Null(g.Opti);

        GameConfig.SetLayer(g, false, "DlssNr", "Enabled", null, baseSetsKey: true);
        Assert.True(Assert.Single(g.Opti!).Unset);

        GameConfig.SetLayer(g, false, "DlssNr", "Enabled", "false", baseSetsKey: true);
        var e = Assert.Single(g.Opti!);
        Assert.False(e.Unset);
        Assert.Equal("false", e.Value);

        GameConfig.ClearLayer(g, false, "dlssnr", "ENABLED");
        Assert.Null(g.Opti);
    }

    [Fact]
    public void ClearLayer_KeepsTheOtherEntries_AndTargetsTheRightFile()
    {
        var g = new GameOverride();
        GameConfig.SetLayer(g, false, "A", "x", "1", true);
        GameConfig.SetLayer(g, false, "A", "y", "2", true);
        GameConfig.SetLayer(g, true, "A", "x", "3", true);
        GameConfig.ClearLayer(g, false, "A", "x");
        Assert.Equal(["A/y=2"], Flat(g.Opti!));
        Assert.Equal(["A/x=3"], Flat(g.ReShade!));
    }

    // ---------- presets ----------

    [Fact]
    public void SavePreset_GameOnly_BecomesTheBaseWithTheEffectiveConfig_AndClearsTheLayer()
    {
        var s = Settings();
        var g = s.For(Game);
        g.Opti = [O("DlssNr", "Intensity", "0.5"), Unset("DlssNr", "Enabled")];
        var before = Flat(GameConfig.EffectiveOpti(s, Game));

        Assert.Null(GameConfig.SavePreset(s, Game, "  Tuned ", gameOnly: true));

        Assert.Equal("Tuned", g.Preset);
        Assert.Null(g.Opti);
        Assert.Null(g.ReShade);
        Assert.Equal("Tuned", Assert.Single(g.Presets!).Name);
        Assert.Equal(before, Flat(GameConfig.EffectiveOpti(s, Game)));
        Assert.DoesNotContain(s.Presets, p => p.Name == "Tuned");
        Assert.Single(s.Presets);
    }

    [Fact]
    public void SavePreset_AllGames_GoesToTheGlobalList()
    {
        var s = Settings();
        s.For(Game).Opti = [O("DlssNr", "Intensity", "0.5")];

        Assert.Null(GameConfig.SavePreset(s, Game, "Shared", gameOnly: false));

        Assert.Contains(s.Presets, p => p.Name == "Shared");
        Assert.Null(s.Games[Game].Presets);
        Assert.Equal("Shared", s.PresetFor(Game)!.Name);
        s.For("game-b").Preset = "Shared";
        Assert.Contains("DlssNr/Intensity=0.5", Flat(GameConfig.EffectiveOpti(s, "game-b")));
    }

    [Fact]
    public void SavePreset_SameScopeReplaces()
    {
        var s = Settings();
        GameConfig.SavePreset(s, Game, "Mine", true);
        s.For(Game).Opti = [O("DlssNr", "Intensity", "0.5")];
        GameConfig.SavePreset(s, Game, "MINE", true);
        var p = Assert.Single(s.Games[Game].Presets!);
        Assert.Contains("DlssNr/Intensity=0.5", Flat(p.Opti));

        GameConfig.SavePreset(s, Game, "Shared", false);
        GameConfig.SavePreset(s, "game-b", "shared", false);
        Assert.Single(s.Presets, x => x.Name.Equals("shared", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Global")]
    [InlineData("global")]
    [InlineData(ConfigProfile.RecommendedName)]
    public void SavePreset_GameOnly_CannotShareANameWithAGlobalOrBuiltInPreset(string name)
    {
        var s = Settings();
        Assert.NotNull(GameConfig.SavePreset(s, Game, name, gameOnly: true));
        Assert.Null(s.Games.GetValueOrDefault(Game)?.Presets);
    }

    [Theory]
    [InlineData(ConfigProfile.RecommendedName)]
    [InlineData("MINE")]
    public void SavePreset_Global_CannotShareANameWithABuiltInOrAnotherGamesPreset(string name)
    {
        var s = Settings();
        s.For("game-b").Presets = [new ConfigPreset { Name = "Mine" }];
        var count = s.Presets.Count;
        Assert.NotNull(GameConfig.SavePreset(s, Game, name, gameOnly: false));
        Assert.Equal(count, s.Presets.Count);
        Assert.NotNull(GameConfig.GlobalNameError(s, name));
    }

    [Fact]
    public void DeleteLocalPreset_ClearsTheGamesPresetOnlyWhenItUsedIt()
    {
        var s = Settings();
        GameConfig.SavePreset(s, Game, "Mine", true);
        GameConfig.SavePreset(s, Game, "Other", true);
        Assert.Equal("Other", s.Games[Game].Preset);

        Assert.True(GameConfig.DeleteLocalPreset(s, Game, "Mine"));
        Assert.Equal("Other", s.Games[Game].Preset);

        Assert.True(GameConfig.DeleteLocalPreset(s, Game, "Other"));
        Assert.Null(s.Games[Game].Preset);
        Assert.Null(s.Games[Game].Presets);
        Assert.Null(s.PresetFor(Game));
        Assert.False(GameConfig.DeleteLocalPreset(s, Game, "Other"));
    }

    [Fact]
    public void MakeGlobal_MovesThePreset_AndKeepsTheGameOnIt()
    {
        var s = Settings();
        GameConfig.SavePreset(s, Game, "Mine", true);

        Assert.Null(GameConfig.MakeGlobal(s, Game, "Mine"));

        Assert.Null(s.Games[Game].Presets);
        Assert.Contains(s.Presets, p => p.Name == "Mine");
        Assert.Equal("Mine", s.PresetFor(Game)!.Name);
    }

    [Fact]
    public void MakeGlobal_FailsOnANameClash_AndChangesNothing()
    {
        var s = Settings();
        GameConfig.SavePreset(s, Game, "Mine", true);
        s.For("game-b").Presets = [new ConfigPreset { Name = "Mine" }];
        Assert.NotNull(GameConfig.MakeGlobal(s, Game, "Mine"));
        Assert.Single(s.Games[Game].Presets!);

        s.Games["game-b"].Presets = null;
        s.Presets.Add(new ConfigPreset { Name = "mine" });
        Assert.NotNull(GameConfig.MakeGlobal(s, Game, "Mine"));
        Assert.Single(s.Games[Game].Presets!);
        Assert.NotNull(GameConfig.MakeGlobal(s, Game, "Missing"));
    }

    [Fact]
    public void ResetLayer_LeavesTheBaseAndModeAlone()
    {
        var s = Settings();
        var g = s.For(Game);
        g.Preset = "Global";
        g.IniMode = "apply";
        g.Opti = [O("A", "x", "1")];
        g.ReShade = [Unset("A", "y")];
        GameConfig.ResetLayer(g);
        Assert.Null(g.Opti);
        Assert.Null(g.ReShade);
        Assert.Equal("Global", g.Preset);
        Assert.Equal("apply", g.IniMode);
    }

    // ---------- ini mode ----------

    [Fact]
    public void EffectiveIniMode_FollowsTheGlobalMode_UnlessTheGameSetsOne()
    {
        var s = Settings();
        s.IniMode = "apply";
        Assert.Equal("apply", GameConfig.EffectiveIniMode(s, Game));

        s.For(Game).IniMode = "fresh";
        Assert.Equal("fresh", GameConfig.EffectiveIniMode(s, Game));
        Assert.Equal("apply", GameConfig.EffectiveIniMode(s, "game-b"));

        s.For(Game).IniMode = "bogus";
        Assert.Equal("apply", GameConfig.EffectiveIniMode(s, Game));
    }

    [Fact]
    public void IniModeValues_AreTheOnesTheSettingsListOffers()
    {
        Assert.Equal(SettingsOptions.IniModes.Select(m => m.Value), GameConfig.IniModes);
    }

    // ---------- merge ----------

    private const string Release = "[DlssNr]\nEnabled=auto\nIntensity=auto\n[Menu]\nShortcutKey=auto\n";

    [Fact]
    public void Merge_UnsetOnReinstall_RestoresTheReleasesValue()
    {
        var baseProfile = new[] { O("DlssNr", "Intensity", "1.5"), O("Menu", "ShortcutKey", "0x2e") };
        var first = ConfigProfile.Merge(Release, null, baseProfile, carryOver: true);
        var firstIni = IniFile.Parse(first.Text);
        Assert.Equal("1.5", firstIni.Get("DlssNr", "Intensity"));

        var layered = ConfigProfile.Layer(baseProfile, [Unset("DlssNr", "Intensity")]);
        var second = ConfigProfile.Merge(Release, first.Text, layered, carryOver: true, lastApplied: first.Applied);

        var ini = IniFile.Parse(second.Text);
        Assert.Equal("auto", ini.Get("DlssNr", "Intensity"));
        Assert.Equal("0x2e", ini.Get("Menu", "ShortcutKey"));
        Assert.DoesNotContain("DlssNr/Intensity", second.Applied.Keys);
    }

    [Fact]
    public void Merge_UnsetOnReinstall_KeepsAValueTheUserChangedInGame()
    {
        var baseProfile = new[] { O("DlssNr", "Intensity", "1.5") };
        var first = ConfigProfile.Merge(Release, null, baseProfile, carryOver: true);
        var edited = first.Text.Replace("Intensity=1.5", "Intensity=0.9");

        var second = ConfigProfile.Merge(Release, edited, ConfigProfile.Layer(baseProfile, [Unset("DlssNr", "Intensity")]), carryOver: true, lastApplied: first.Applied);

        Assert.Equal("0.9", IniFile.Parse(second.Text).Get("DlssNr", "Intensity"));
    }

    // ---------- JSON ----------

    [Fact]
    public void Json_UnsetIsOnlyWrittenWhenTrue()
    {
        var s = new AppSettings();
        s.For(Game).Opti = [O("A", "x", "1"), Unset("A", "y")];
        var json = JsonSerializer.Serialize(s, JsonCtx.Default.AppSettings);

        var opti = JsonDocument.Parse(json).RootElement.GetProperty("Games").GetProperty(Game).GetProperty("Opti");
        Assert.False(opti[0].TryGetProperty("Unset", out _));
        Assert.True(opti[1].GetProperty("Unset").GetBoolean());
    }

    [Fact]
    public void Json_GameLayerModeAndLocalPresets_RoundTrip_AndOldFilesLoadWithNulls()
    {
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "dlssu-gameconfig-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.Ensure();
        try
        {
            var s = new AppSettings();
            var g = s.For(Game);
            g.Preset = "Mine";
            g.IniMode = "fresh";
            g.Opti = [O("A", "x", "1"), Unset("A", "y")];
            g.ReShade = [O("B", "z", "2")];
            g.Presets = [new ConfigPreset { Name = "Mine", Opti = [Unset("A", "q"), O("A", "r", "3")], ReShade = [O("B", "s", "4")] }];
            s.For("plain").Preset = "Global";
            s.Save();

            var loaded = AppSettings.Load();
            var l = loaded.Games[Game];
            Assert.Equal("fresh", l.IniMode);
            Assert.Equal("Mine", l.Preset);
            Assert.Equal(["A/x=1", "A/y="], Flat(l.Opti!));
            Assert.True(l.Opti![1].Unset);
            Assert.False(l.Opti[0].Unset);
            Assert.Equal(["B/z=2"], Flat(l.ReShade!));
            var local = Assert.Single(l.Presets!);
            Assert.Equal("Mine", local.Name);
            Assert.True(local.Opti[0].Unset);
            Assert.Equal(["B/s=4"], Flat(local.ReShade));
            Assert.Equal(local.Name, loaded.PresetFor(Game)!.Name);

            var plain = loaded.Games["plain"];
            Assert.Null(plain.Opti);
            Assert.Null(plain.ReShade);
            Assert.Null(plain.IniMode);
            Assert.Null(plain.Presets);
        }
        finally
        {
            Directory.Delete(AppPaths.Root, true);
        }
    }
}
