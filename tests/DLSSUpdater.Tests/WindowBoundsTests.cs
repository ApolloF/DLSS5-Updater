using System.Windows;
using DLSSUpdater.Core;
using DLSSUpdater.Views;

namespace DLSSUpdater.Tests;

public sealed class WindowBoundsTests : IDisposable
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);
    private static readonly Size Min = new(1180, 680);
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-bounds-" + Guid.NewGuid().ToString("N"));

    public WindowBoundsTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
    }

    public void Dispose() => Directory.Delete(_tmp, true);

    [Fact]
    public void OnScreen_RestoresTheSavedRect() =>
        Assert.Equal(new Rect(100, 50, 1400, 900), WindowBounds.Restore(100, 50, 1400, 900, Screen, Min));

    [Fact]
    public void NothingSaved_KeepsTheDefaults() =>
        Assert.Null(WindowBounds.Restore(null, null, null, null, Screen, Min));

    [Theory]
    [InlineData(3000, 100)]   // on a monitor that is gone
    [InlineData(100, -500)]   // title bar above the screen
    [InlineData(100, 1070)]   // title bar below the screen
    [InlineData(1850, 100)]   // only a sliver on the right edge
    public void OffScreen_KeepsTheDefaults(double left, double top) =>
        Assert.Null(WindowBounds.Restore(left, top, 1240, 800, Screen, Min));

    [Fact]
    public void PartlyOffScreen_WithTheTitleBarReachable_IsKept() =>
        Assert.NotNull(WindowBounds.Restore(-600, 200, 1240, 800, Screen, Min));

    [Fact]
    public void TooSmall_GrowsToTheMinimum() =>
        Assert.Equal(new Rect(10, 10, 1180, 680), WindowBounds.Restore(10, 10, 800, 400, Screen, Min));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    public void Garbage_KeepsTheDefaults(double width) =>
        Assert.Null(WindowBounds.Restore(10, 10, width, 800, Screen, Min));

    [Fact]
    public void WindowBounds_SurviveARestart()
    {
        new AppSettings { WindowLeft = 10, WindowTop = 20, WindowWidth = 1300, WindowHeight = 850, WindowMaximized = true }.Save();
        var s = AppSettings.Load();
        Assert.Equal((10d, 20d, 1300d, 850d, true), (s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight, s.WindowMaximized));
    }
}
