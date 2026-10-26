using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>The one-neural-consumer rule (docs/advisor.md, decision table row 18), as one conflict line per clash.</summary>
public sealed class NrConsumerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dlssu-nrc-" + Guid.NewGuid().ToString("N")[..8]);

    public NrConsumerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(NrAddon.Dfc, NrAddon.RenoDx, "Deep Fried Chicken does nothing")]
    [InlineData(NrAddon.RenoDx, NrAddon.Dfc, "Deep Fried Chicken does nothing")]
    [InlineData(NrAddon.Dfc, NrAddon.Bridge, "Deep Fried Chicken does nothing")]
    [InlineData(NrAddon.Lecram, NrAddon.OptiScaler, "OptiScaler takes over")]
    [InlineData(NrAddon.OptiScaler, NrAddon.Dfc, "Deep Fried Chicken does nothing")]
    [InlineData(NrAddon.Uplift, NrAddon.Bridge, "the bridge is for games with their own DLSS")]
    [InlineData(NrAddon.RenoDx, NrAddon.Uplift, "only one neural route")]
    public void TwoConsumers_Conflict_WithOneLineSayingWhy(NrAddon adding, NrAddon present, string why)
    {
        var conflict = NrAddons.Conflict(adding, [present]);

        Assert.NotNull(conflict);
        Assert.Contains("don't work well in the same game", conflict);
        Assert.Contains(why, conflict);
        Assert.DoesNotContain('\n', conflict);
        Assert.True(char.IsUpper(conflict[0]));
    }

    [Fact]
    public void ShortFuseWithLecram_IsAConflictToo_WithItsOwnReason()
    {
        Assert.Contains("NR runs twice", NrAddons.Conflict(NrAddon.Lecram, [NrAddon.RenoDx]));
        Assert.NotNull(NrAddons.Conflict(NrAddon.Bridge, [NrAddon.RenoDx]));
        Assert.NotNull(NrAddons.Conflict(NrAddon.OptiScaler, [NrAddon.Lecram]));
    }

    [Fact]
    public void ARouteNeverConflictsWithItself_OrAgainstNothing()
    {
        Assert.Null(NrAddons.Conflict(NrAddon.Dfc, []));
        Assert.Null(NrAddons.Conflict(NrAddon.Dfc, [NrAddon.Dfc]));
    }

    [Fact]
    public void Feeder_WorksWithDfcAlone()
    {
        Assert.Null(NrAddons.FeederConflict([NrAddon.Dfc]));
    }

    [Theory]
    [InlineData(NrAddon.OptiScaler, "stock OptiScaler")]
    [InlineData(NrAddon.Bridge, "DX11 bridge")]
    [InlineData(NrAddon.RenoDx, "replaces Feeder")]
    [InlineData(NrAddon.Lecram, "only pairs Feeder with Deep Fried Chicken")]
    [InlineData(NrAddon.Uplift, "only pairs Feeder with Deep Fried Chicken")]
    public void Feeder_ConflictsNextToAnyOtherConsumer_EvenBesideDfc(NrAddon other, string why)
    {
        Assert.Contains(why, NrAddons.FeederConflict([other]));
        Assert.Contains(why, NrAddons.FeederConflict([NrAddon.Dfc, other]));
    }

    [Fact]
    public void Feeder_WithoutAnyConsumer_SaysToTurnOnDfc()
    {
        Assert.Equal("DLSS5-Feeder needs one neural consumer in the game: turn on Deep Fried Chicken.", NrAddons.FeederConflict([]));
    }

    [Fact]
    public void PresentIn_ReadsTheFilesAndTheRecord()
    {
        foreach (var f in new[] { "renodx-dlssfix.addon64", "dlss5-nr.addon64", "deep-fried-chicken.addon64", "dlss5-dx11-bridge.addon64", "OptiScaler.ini" })
            File.WriteAllText(Path.Combine(_dir, f), "x");

        Assert.Equal([NrAddon.RenoDx, NrAddon.Lecram, NrAddon.Dfc, NrAddon.Bridge, NrAddon.OptiScaler], NrAddons.PresentIn(_dir, null));
        Assert.Equal([NrAddon.Uplift], NrAddons.PresentIn(Path.Combine(_dir, "empty"), new InstallManifest { Uplift = true }));
        Assert.Empty(NrAddons.PresentIn(Path.Combine(_dir, "empty"), null));
    }

    [Fact]
    public void RoutesOf_ListsWhatTheRunLeavesInTheGame()
    {
        Assert.Equal([NrAddon.Dfc], Installer.RoutesOf(new InstallOptions { Mode = InstallMode.ReShadeOnly, Dfc = true }, _dir));
        Assert.Equal([NrAddon.OptiScaler], Installer.RoutesOf(new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true }, _dir));
        Assert.Equal([NrAddon.RenoDx], Installer.RoutesOf(new InstallOptions { Mode = InstallMode.ReShadeOnly, RenoDx = true }, _dir));
        Assert.Equal([NrAddon.OptiScaler, NrAddon.RenoDx, NrAddon.Dfc], Installer.RoutesOf(new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true, RenoDx = true, Dfc = true }, _dir));
        Assert.Empty(Installer.RoutesOf(new InstallOptions { Mode = InstallMode.ReShadeOnly }, _dir));
    }

    [Fact]
    public void RoutesOf_CountsABridgeNothingOfOursRemoves()
    {
        File.WriteAllText(Path.Combine(_dir, "dlss5-bridge.addon64"), "x");

        Assert.Equal([NrAddon.Dfc, NrAddon.Bridge], Installer.RoutesOf(new InstallOptions { Mode = InstallMode.ReShadeOnly, Dfc = true }, _dir));
    }

    [Fact]
    public void Planner_ReturnsABridgeAndFeederClash_InsteadOfThrowing_AndOnlyTheModeStaysAHardStop()
    {
        File.WriteAllText(Path.Combine(_dir, "dlss5-bridge.addon64"), "x");
        var withBridge = Installer.Conflicts(_dir, new InstallOptions { Mode = InstallMode.ReShadeOnly, Dfc = true });
        Assert.Contains("bridge", Assert.Single(withBridge));

        var plain = Path.Combine(_dir, "plain");
        Directory.CreateDirectory(plain);
        Assert.Contains("turn on Deep Fried Chicken", Assert.Single(Installer.Conflicts(plain, new InstallOptions { Mode = InstallMode.ReShadeOnly, Feeder = true })));
        Assert.Empty(Installer.Conflicts(plain, new InstallOptions { Mode = InstallMode.ReShadeOnly, Dfc = true, Feeder = true }));
        Assert.Empty(Installer.Conflicts(plain, new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true }));
        var both = Installer.Conflicts(plain, new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true, Dfc = true });
        Assert.Contains("Deep Fried Chicken does nothing", Assert.Single(both));
    }

    [Fact]
    public void SettingsDefaults_StillIgnoreTheRoutesThatAreNeverDefaults()
    {
        var s = new AppSettings();
        NrAddons.SetDefault(s, NrAddon.RenoDx, true);

        Assert.Equal([NrAddon.RenoDx], NrAddons.Defaults(s));
            }
}
