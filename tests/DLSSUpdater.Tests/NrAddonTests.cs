using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>One NR add-on per game unless the user allows ShortFuse + Lecram together; the F5 default for new installs.</summary>
public sealed class NrAddonTests : IDisposable
{
    public NrAddonTests()
    {
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "dlssu-nraddon-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(AppPaths.Root, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(NrAddon.RenoDx, NrAddon.Lecram)]
    [InlineData(NrAddon.Lecram, NrAddon.RenoDx)]
    [InlineData(NrAddon.Uplift, NrAddon.RenoDx)]
    [InlineData(NrAddon.Dfc, NrAddon.Lecram)]
    [InlineData(NrAddon.RenoDx, NrAddon.Uplift)]
    public void WithoutOverride_TurningOneOnTurnsTheOtherOff(NrAddon turnedOn, NrAddon other) =>
        Assert.Equal([other], NrAddons.ToSwitchOff(turnedOn, [turnedOn, other], allowSeveral: false));

    [Fact]
    public void Override_LetsOnlyShortFuseAndLecramRunTogether()
    {
        Assert.Empty(NrAddons.ToSwitchOff(NrAddon.Lecram, [NrAddon.RenoDx, NrAddon.Lecram], allowSeveral: true));
        Assert.Equal([NrAddon.RenoDx, NrAddon.Lecram], NrAddons.ToSwitchOff(NrAddon.Uplift, [NrAddon.RenoDx, NrAddon.Lecram, NrAddon.Uplift], allowSeveral: true));
        Assert.Equal([NrAddon.RenoDx], NrAddons.ToSwitchOff(NrAddon.Dfc, [NrAddon.RenoDx, NrAddon.Dfc], allowSeveral: true));
    }

    [Fact]
    public void Normalize_KeepsByPriority()
    {
        Assert.Equal(new HashSet<NrAddon> { NrAddon.Lecram }, NrAddons.Normalize([NrAddon.RenoDx, NrAddon.Lecram], false));
        Assert.Equal(new HashSet<NrAddon> { NrAddon.RenoDx, NrAddon.Lecram }, NrAddons.Normalize([NrAddon.RenoDx, NrAddon.Lecram], true));
        Assert.Equal(new HashSet<NrAddon> { NrAddon.Uplift }, NrAddons.Normalize([NrAddon.RenoDx, NrAddon.Lecram, NrAddon.Uplift], true));
        Assert.Empty(NrAddons.Normalize([], false));
    }

    [Fact]
    public void SettingsToggles_AreExclusive_StillAllowNone()
    {
        var s = new AppSettings();
        NrAddons.SetDefault(s, NrAddon.RenoDx, true);
        NrAddons.SetDefault(s, NrAddon.Lecram, true);
        Assert.Equal((false, true, false), (s.InstallRenoDx, s.InstallLecram, s.InstallUplift));
        NrAddons.SetDefault(s, NrAddon.Uplift, true);
        Assert.Equal((false, false, true), (s.InstallRenoDx, s.InstallLecram, s.InstallUplift));
        NrAddons.SetDefault(s, NrAddon.Uplift, false);
        Assert.Equal((false, false, false), (s.InstallRenoDx, s.InstallLecram, s.InstallUplift));
        Assert.Null(NrAddons.Warning(s.InstallRenoDx, s.InstallLecram));
    }

    [Fact]
    public void SettingsOverride_AllowsThePair_WarnsAndTurningItOffKeepsLecram()
    {
        var s = new AppSettings();
        NrAddons.SetAllowSeveral(s, true);
        NrAddons.SetDefault(s, NrAddon.RenoDx, true);
        NrAddons.SetDefault(s, NrAddon.Lecram, true);
        Assert.Equal((true, true), (s.InstallRenoDx, s.InstallLecram));
        Assert.Contains("NR runs twice", NrAddons.Warning(s.InstallRenoDx, s.InstallLecram));

        NrAddons.SetAllowSeveral(s, false);
        Assert.Equal((false, true, false), (s.InstallRenoDx, s.InstallLecram, s.AllowSeveralNrAddons));
    }

    [Theory]
    [InlineData(true, true, false, true, true, false, true)]   // the pair keeps working: the override is turned on
    [InlineData(true, true, true, false, false, true, false)]  // Uplift wins, as installs already did
    [InlineData(true, false, true, false, false, true, false)]
    [InlineData(false, true, false, false, true, false, false)] // one add-on: nothing changes
    public void Migration_KeepsOldSettingsWorking(bool sf, bool lecram, bool uplift, bool sfAfter, bool lecramAfter, bool upliftAfter, bool allowAfter)
    {
        var s = new AppSettings { InstallRenoDx = sf, InstallLecram = lecram, InstallUplift = uplift };
        var changed = NrAddons.Migrate(s);
        Assert.Equal((sfAfter, lecramAfter, upliftAfter, allowAfter), (s.InstallRenoDx, s.InstallLecram, s.InstallUplift, s.AllowSeveralNrAddons));
        Assert.Equal((sf, lecram, uplift, false) != (sfAfter, lecramAfter, upliftAfter, allowAfter), changed);
        Assert.False(NrAddons.Migrate(s));
    }

    [Fact]
    public void Load_MigratesAndSavesOldSettings()
    {
        FileUtil.AtomicWriteText(AppPaths.SettingsFile, """{"InstallRenoDx":true,"InstallLecram":true}""");
        var s = AppSettings.Load();
        Assert.True(s.AllowSeveralNrAddons);
        Assert.Contains("\"AllowSeveralNrAddons\": true", File.ReadAllText(AppPaths.SettingsFile));
    }

    [Fact]
    public void FreshSettings_DefaultToF5_SavedChoiceIsKept()
    {
        Assert.Same(OptiSources.F5, OptiSources.For(AppSettings.Load().OptiSource));
        Assert.Same(OptiSources.F5, OptiSources.For(new AppSettings().OptiSource));
        Assert.False(new AppSettings().AllowSeveralNrAddons);

        FileUtil.AtomicWriteText(AppPaths.SettingsFile, $$"""{"OptiSource":"{{ComponentStore.OptiRepo}}"}""");
        Assert.Same(OptiSources.Default, OptiSources.For(AppSettings.Load().OptiSource));
    }
}
