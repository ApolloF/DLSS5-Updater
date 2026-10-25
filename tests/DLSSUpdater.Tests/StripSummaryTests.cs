using DLSSUpdater.Core;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

public sealed class StripSummaryTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-strip-" + Guid.NewGuid().ToString("N"));

    public StripSummaryTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
    }

    public void Dispose() => Directory.Delete(_tmp, true);

    [Theory]
    [InlineData(0, 0, "Components: all current")]
    [InlineData(1, 0, "Components: 1 new release")]
    [InlineData(2, 0, "Components: 2 new releases")]
    [InlineData(0, 1, "Components: 1 game needs updates")]
    [InlineData(1, 4, "Components: 1 new release · 4 games need updates")]
    public void Text_CountsNewReleasesAndGamesWithUpdates(int newReleases, int games, string expected) =>
        Assert.Equal(expected, ComponentStripSummary.Text(newReleases, games));

    [Fact]
    public void StripExpanded_IsOffByDefault_AndSurvivesARestart()
    {
        Assert.False(new AppSettings().StripExpanded);
        new AppSettings { StripExpanded = true }.Save();
        Assert.True(AppSettings.Load().StripExpanded);
    }
}
