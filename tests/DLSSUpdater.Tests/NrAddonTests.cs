using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Settings defaults stay on one NR add-on; any mix is a warning per game. The F5 default for new installs.</summary>
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
    [InlineData(NrAddon.RenoDx, NrAddon.Lecram, "NR runs twice")]
    [InlineData(NrAddon.Lecram, NrAddon.RenoDx, "NR runs twice")]
    [InlineData(NrAddon.Uplift, NrAddon.RenoDx, "only one neural route")]
    [InlineData(NrAddon.Dfc, NrAddon.Lecram, "Deep Fried Chicken does nothing")]
    public void AnyMix_IsAConflictWithAReason_NeverARefusal(NrAddon a, NrAddon b, string why)
    {
        Assert.Contains(why, NrAddons.Conflict(a, [b]));
        Assert.Single(NrAddons.Conflicts([a, b]));
    }

    [Fact]
    public void Conflicts_ListsEachClashingPairOnce()
    {
        Assert.Empty(NrAddons.Conflicts([NrAddon.Dfc]));
        Assert.Empty(NrAddons.Conflicts([]));
        Assert.Equal(2, NrAddons.Conflicts([NrAddon.RenoDx, NrAddon.Lecram, NrAddon.Uplift]).Count);
    }

    [Fact]
    public void SettingsToggles_LeaveTheOthersOn_AndReportTheClash()
    {
        var s = new AppSettings();
        NrAddons.SetDefault(s, NrAddon.RenoDx, true);
        Assert.Empty(NrAddons.DefaultConflicts(s));
        NrAddons.SetDefault(s, NrAddon.Lecram, true);
        Assert.Equal((true, true, false), (s.InstallRenoDx, s.InstallLecram, s.InstallUplift));
        Assert.Single(NrAddons.DefaultConflicts(s));
        NrAddons.SetDefault(s, NrAddon.Uplift, true);
        Assert.Equal((true, true, true), (s.InstallRenoDx, s.InstallLecram, s.InstallUplift));
        Assert.Equal(3, NrAddons.DefaultConflicts(s).Count);
        NrAddons.SetDefault(s, NrAddon.Uplift, false);
        NrAddons.SetDefault(s, NrAddon.Lecram, false);
        Assert.Equal((true, false, false), (s.InstallRenoDx, s.InstallLecram, s.InstallUplift));
    }

    [Fact]
    public void OldSettingsFile_WithSeveralAddonsOn_KeepsEveryDefault()
    {
        FileUtil.AtomicWriteText(AppPaths.SettingsFile, """{"InstallRenoDx":true,"InstallLecram":true,"AllowSeveralNrAddons":true,"SomethingFromTheFuture":1}""");
        var s = AppSettings.Load();
        Assert.True(s.InstallLecram);
        Assert.True(s.InstallRenoDx);
    }

    [Fact]
    public void OldSettingsFile_WithOneAddonOn_KeepsIt()
    {
        FileUtil.AtomicWriteText(AppPaths.SettingsFile, """{"InstallRenoDx":false,"InstallLecram":true,"AllowSeveralNrAddons":false}""");
        var s = AppSettings.Load();
        Assert.True(s.InstallLecram);
        Assert.False(s.InstallRenoDx);
    }

    [Fact]
    public void FreshSettings_DefaultToF5_SavedChoiceIsKept()
    {
        Assert.Same(OptiSources.F5, OptiSources.For(AppSettings.Load().OptiSource));
        Assert.Same(OptiSources.F5, OptiSources.For(new AppSettings().OptiSource));

        FileUtil.AtomicWriteText(AppPaths.SettingsFile, $$"""{"OptiSource":"{{ComponentStore.OptiRepo}}"}""");
        Assert.Same(OptiSources.Default, OptiSources.For(AppSettings.Load().OptiSource));
    }
}
