using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Method cards and the component table: which card the table installs, and the rows no card covers.</summary>
public sealed class MethodMatchTests
{
    private static MethodOption Opt(Method m, InstallMode? mode, params string[] components) =>
        new(m, m.ToString(), Availability.Available, "reason", components, [], mode, false);

    private static readonly MethodOption Dlss = Opt(Method.DlssUpdate, null, ComponentRegistry.Dlss);
    private static readonly MethodOption Neural = Opt(Method.NeuralAddon, InstallMode.ReShadeOnly, ComponentRegistry.ReShade, ComponentRegistry.DlssNr, ComponentRegistry.Lecram);
    private static readonly MethodOption OptiNr = Opt(Method.OptiScalerNr, InstallMode.OptiScaler, ComponentRegistry.OptiScaler, ComponentRegistry.DlssNr);
    private static readonly MethodOption Opti = Opt(Method.OptiScaler, InstallMode.OptiScaler, ComponentRegistry.OptiScaler);
    private static readonly MethodOption Mfg = Opt(Method.MfgUnlock, null, ComponentRegistry.MfgUnlock);
    private static readonly MethodOption[] Choices = [Dlss, Neural, OptiNr, Opti];

    /// <summary>Every table row shown; the listed ids are on.</summary>
    private static Dictionary<string, TableRow> Table(params string[] on) =>
        ComponentTable.Rows.ToDictionary(r => r.Id, r => new TableRow(true, on.Contains(r.Id)), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void TheOwnersCase_NeuralRowsOn_PicksTheNeuralCard_NotTheDlssUpdate()
    {
        var table = Table(ComponentRegistry.ReShade, ComponentRegistry.MfgUnlock, ComponentRegistry.DlssNr, ComponentRegistry.Lecram,
            ComponentRegistry.Dlss, ComponentRegistry.Streamline);

        var picked = MethodMatch.Picked(Choices, table, InstallMode.ReShadeOnly, clicked: Method.DlssUpdate);

        Assert.Same(Neural, picked);
        Assert.Equal([ComponentRegistry.Dlss, ComponentRegistry.Streamline], MethodMatch.Outside(picked, [Mfg], table));
        Assert.Equal("DLSS (SR · RR · FG) · Streamline (for Dynamic MFG)", MethodMatch.Names(MethodMatch.Outside(picked, [Mfg], table)));
    }

    [Fact]
    public void TheOptiScalerTick_IsTheInstallMode()
    {
        Assert.Equal(InstallMode.OptiScaler, ComponentTable.ModeFor(true));
        Assert.Equal(InstallMode.ReShadeOnly, ComponentTable.ModeFor(false));
    }

    [Theory]
    [InlineData("opti", InstallMode.OptiScaler, true, true, true)]
    [InlineData("opti", InstallMode.ReShadeOnly, true, true, false)]
    [InlineData("reshade", InstallMode.OptiScaler, true, true, null)]
    [InlineData("reshade", InstallMode.ReShadeOnly, true, false, true)]
    [InlineData("lecram", InstallMode.OptiScaler, false, true, false)]
    [InlineData("feeder", InstallMode.OptiScaler, false, true, false)]
    [InlineData("lecram", InstallMode.ReShadeOnly, true, true, null)]
    [InlineData("mfg", InstallMode.OptiScaler, true, true, null)]
    public void RowRules_FollowTheMode(string key, InstallMode mode, bool visible, bool editable, bool? on) =>
        Assert.Equal(new ComponentTable.RowRule(visible, editable, on), ComponentTable.Rule(key, mode));

    [Fact]
    public void ACardNeedingTheOtherMode_DoesNotMatch()
    {
        var table = Table(ComponentRegistry.ReShade, ComponentRegistry.DlssNr, ComponentRegistry.Lecram);

        Assert.False(MethodMatch.Matches(Neural, table, InstallMode.OptiScaler));
        Assert.True(MethodMatch.Matches(Neural, table, InstallMode.ReShadeOnly));
    }

    [Fact]
    public void HiddenRows_DoNotCount_ButACardNeedsOneShownRowOn()
    {
        var table = Table(ComponentRegistry.ReShade, ComponentRegistry.DlssNr);
        table[ComponentRegistry.Lecram] = new TableRow(false, false);
        Assert.True(MethodMatch.Matches(Neural, table, InstallMode.ReShadeOnly));

        var none = Table();
        none[ComponentRegistry.Dlss] = new TableRow(false, true);
        Assert.False(MethodMatch.Matches(Dlss, none, InstallMode.OptiScaler));
    }

    [Fact]
    public void ATie_GoesToTheClickedCard_ElseToTheHigherRanked()
    {
        var table = Table(ComponentRegistry.OptiScaler, ComponentRegistry.Dlss);

        Assert.Same(Dlss, MethodMatch.Picked(Choices, table, InstallMode.OptiScaler));
        Assert.Same(Opti, MethodMatch.Picked(Choices, table, InstallMode.OptiScaler, Method.OptiScaler));
        Assert.Equal([ComponentRegistry.Dlss], MethodMatch.Outside(Opti, [], table));
    }

    [Fact]
    public void NoMatchingCard_IsACustomSelection_ListingEveryRowOn()
    {
        var table = Table(ComponentRegistry.ReShade, ComponentRegistry.Streamline, ComponentRegistry.MfgUnlock);

        var picked = MethodMatch.Picked(Choices, table, InstallMode.ReShadeOnly);

        Assert.Null(picked);
        Assert.Equal([ComponentRegistry.ReShade, ComponentRegistry.MfgUnlock, ComponentRegistry.Streamline], MethodMatch.Outside(null, [], table));
        Assert.Equal([ComponentRegistry.ReShade, ComponentRegistry.Streamline], MethodMatch.Outside(null, [Mfg], table));
    }

    [Fact]
    public void ACardThatCantBeSelected_NeverMatches()
    {
        var notPossible = Dlss with { Availability = Availability.NotPossible };

        Assert.False(MethodMatch.Matches(notPossible, Table(ComponentRegistry.Dlss), InstallMode.OptiScaler));
    }

    [Fact]
    public void EveryComponentAMethodCanTurnOn_HasATableRow()
    {
        foreach (var id in new[] { ComponentRegistry.OptiScaler, ComponentRegistry.ReShade, ComponentRegistry.MfgUnlock, ComponentRegistry.DlssNr, ComponentRegistry.RenoDx,
                     ComponentRegistry.Lecram, ComponentRegistry.Uplift, ComponentRegistry.Dfc, ComponentRegistry.Feeder, ComponentRegistry.Dlss, ComponentRegistry.Streamline })
            Assert.NotNull(ComponentTable.KeyOf(id));
        Assert.Equal(ComponentTable.Rows.Count, ComponentTable.Rows.Select(r => r.Key).Distinct().Count());
    }
}
