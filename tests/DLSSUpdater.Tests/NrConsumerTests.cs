using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>The one-neural-consumer rule (docs/research/methods-2026-10.md section 2 and 8 row 18), as one line per refusal.</summary>
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
    [InlineData(NrAddon.Uplift, NrAddon.Bridge, "only one neural route")]
    [InlineData(NrAddon.RenoDx, NrAddon.Uplift, "only one neural route")]
    public void TwoConsumers_AreRefused_WithOneLineSayingWhy(NrAddon adding, NrAddon present, string why)
    {
        var refusal = NrAddons.Refusal(adding, [present], allowSeveral: false);

        Assert.NotNull(refusal);
        Assert.Contains("can't be in the same game", refusal);
        Assert.Contains(why, refusal);
        Assert.DoesNotContain('\n', refusal);
        Assert.True(char.IsUpper(refusal[0]));
    }

    [Fact]
    public void ShortFuseWithLecram_IsTheOnlyPairTheOverrideAllows_AndEverythingElseStaysRefused()
    {
        Assert.Null(NrAddons.Refusal(NrAddon.Lecram, [NrAddon.RenoDx], allowSeveral: true));
        Assert.NotNull(NrAddons.Refusal(NrAddon.Lecram, [NrAddon.RenoDx], allowSeveral: false));
        Assert.NotNull(NrAddons.Refusal(NrAddon.Bridge, [NrAddon.RenoDx], allowSeveral: true));
        Assert.NotNull(NrAddons.Refusal(NrAddon.OptiScaler, [NrAddon.Lecram], allowSeveral: true));
    }

    [Fact]
    public void ARouteIsNeverRefusedAgainstItself_OrAgainstNothing()
    {
        Assert.Null(NrAddons.Refusal(NrAddon.Dfc, [], false));
        Assert.Null(NrAddons.Refusal(NrAddon.Dfc, [NrAddon.Dfc], false));
    }

    [Fact]
    public void Feeder_WorksWithDfcAlone()
    {
        Assert.Null(NrAddons.FeederRefusal([NrAddon.Dfc]));
    }

    [Theory]
    [InlineData(NrAddon.OptiScaler, "stock OptiScaler")]
    [InlineData(NrAddon.Bridge, "DX11 bridge")]
    [InlineData(NrAddon.RenoDx, "replaces Feeder")]
    [InlineData(NrAddon.Lecram, "only pairs Feeder with Deep Fried Chicken")]
    [InlineData(NrAddon.Uplift, "only pairs Feeder with Deep Fried Chicken")]
    public void Feeder_IsRefusedNextToAnyOtherConsumer_EvenBesideDfc(NrAddon other, string why)
    {
        Assert.Contains(why, NrAddons.FeederRefusal([other]));
        Assert.Contains(why, NrAddons.FeederRefusal([NrAddon.Dfc, other]));
    }

    [Fact]
    public void Feeder_WithoutAnyConsumer_SaysToTurnOnDfc()
    {
        Assert.Equal("DLSS5-Feeder needs one neural consumer in the game: turn on Deep Fried Chicken.", NrAddons.FeederRefusal([]));
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
        Assert.Empty(Installer.RoutesOf(new InstallOptions { Mode = InstallMode.ReShadeOnly }, _dir));
    }

    [Fact]
    public void RoutesOf_CountsABridgeNothingOfOursRemoves()
    {
        File.WriteAllText(Path.Combine(_dir, "dlss5-bridge.addon64"), "x");

        Assert.Equal([NrAddon.Dfc, NrAddon.Bridge], Installer.RoutesOf(new InstallOptions { Mode = InstallMode.ReShadeOnly, Dfc = true }, _dir));
    }

    [Fact]
    public void Planner_RefusesABridgeBesideAnyRoute_AndFeederWithoutDfc_AndFeederOutsideReShadeMode()
    {
        File.WriteAllText(Path.Combine(_dir, "dlss5-bridge.addon64"), "x");
        var withBridge = Assert.Throws<InvalidOperationException>(() =>
            Installer.EnsureOneNeuralConsumer(_dir, new InstallOptions { Mode = InstallMode.ReShadeOnly, Dfc = true }));
        Assert.Contains("bridge files", withBridge.Message);

        var plain = Path.Combine(_dir, "plain");
        Directory.CreateDirectory(plain);
        Assert.Contains("turn on Deep Fried Chicken", Assert.Throws<InvalidOperationException>(() =>
            Installer.EnsureOneNeuralConsumer(plain, new InstallOptions { Mode = InstallMode.ReShadeOnly, Feeder = true })).Message);
        Assert.Contains("ReShade + add-ons mode", Assert.Throws<InvalidOperationException>(() =>
            Installer.EnsureOneNeuralConsumer(plain, new InstallOptions { Mode = InstallMode.OptiScaler, Dfc = true, Feeder = true })).Message);
        Installer.EnsureOneNeuralConsumer(plain, new InstallOptions { Mode = InstallMode.ReShadeOnly, Dfc = true, Feeder = true });
        Installer.EnsureOneNeuralConsumer(plain, new InstallOptions { Mode = InstallMode.OptiScaler, Opti = true });
    }

    [Fact]
    public void SettingsDefaults_StillIgnoreTheRoutesThatAreNeverDefaults()
    {
        var s = new AppSettings();
        NrAddons.SetDefault(s, NrAddon.RenoDx, true);

        Assert.Equal([NrAddon.RenoDx], NrAddons.Defaults(s));
        Assert.Empty(NrAddons.Normalize([NrAddon.Bridge, NrAddon.OptiScaler], false));
    }
}
