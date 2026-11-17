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
    [InlineData(NrAddon.Lecram, NrAddon.OptiScaler, "OptiScaler takes over")]
    [InlineData(NrAddon.OptiScaler, NrAddon.RenoDx, "OptiScaler takes over")]
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
        Assert.Null(NrAddons.Conflict(NrAddon.Uplift, []));
        Assert.Null(NrAddons.Conflict(NrAddon.Uplift, [NrAddon.Uplift]));
    }

    [Fact]
    public void PresentIn_ReadsTheFilesAndTheRecord()
    {
        foreach (var f in new[] { "renodx-dlssfix.addon64", "dlss5-nr.addon64", "dlss5-dx11-bridge.addon64", "OptiScaler.ini" })
            File.WriteAllText(Path.Combine(_dir, f), "x");

        Assert.Equal([NrAddon.RenoDx, NrAddon.Lecram, NrAddon.Bridge, NrAddon.OptiScaler], NrAddons.PresentIn(_dir, null));
        Assert.Equal([NrAddon.Uplift], NrAddons.PresentIn(Path.Combine(_dir, "empty"), new InstallManifest { Uplift = true }));
        Assert.Empty(NrAddons.PresentIn(Path.Combine(_dir, "empty"), null));
    }

    [Fact]
    public void RoutesOf_ListsWhatTheRunLeavesInTheGame()
    {
        Assert.Equal([NrAddon.Uplift], Installer.RoutesOf(new InstallOptions { Mode = InstallMode.ReShadeOnly, Uplift = true }, _dir));
        Assert.Equal([NrAddon.OptiScaler], Installer.RoutesOf(new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true }, _dir));
        Assert.Equal([NrAddon.RenoDx], Installer.RoutesOf(new InstallOptions { Mode = InstallMode.ReShadeOnly, RenoDx = true }, _dir));
        Assert.Equal([NrAddon.OptiScaler, NrAddon.RenoDx, NrAddon.Uplift], Installer.RoutesOf(new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true, RenoDx = true, Uplift = true }, _dir));
        Assert.Empty(Installer.RoutesOf(new InstallOptions { Mode = InstallMode.ReShadeOnly }, _dir));
    }

    [Fact]
    public void RoutesOf_CountsABridgeNothingOfOursRemoves()
    {
        File.WriteAllText(Path.Combine(_dir, "dlss5-bridge.addon64"), "x");

        Assert.Equal([NrAddon.Uplift, NrAddon.Bridge], Installer.RoutesOf(new InstallOptions { Mode = InstallMode.ReShadeOnly, Uplift = true }, _dir));
    }

    [Fact]
    public void Planner_ReturnsABridgeClash_InsteadOfThrowing_AndOnlyTheModeStaysAHardStop()
    {
        File.WriteAllText(Path.Combine(_dir, "dlss5-bridge.addon64"), "x");
        var withBridge = Installer.Conflicts(_dir, new InstallOptions { Mode = InstallMode.ReShadeOnly, Uplift = true });
        Assert.Contains("bridge", Assert.Single(withBridge));

        var plain = Path.Combine(_dir, "plain");
        Directory.CreateDirectory(plain);
        Assert.Empty(Installer.Conflicts(plain, new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true }));
        var both = Installer.Conflicts(plain, new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true, Uplift = true });
        Assert.Contains("OptiScaler takes over", Assert.Single(both));
    }

    [Fact]
    public void SettingsDefaults_StillIgnoreTheRoutesThatAreNeverDefaults()
    {
        var s = new AppSettings();
        NrAddons.SetDefault(s, NrAddon.RenoDx, true);

        Assert.Equal([NrAddon.RenoDx], NrAddons.Defaults(s));
            }
}
