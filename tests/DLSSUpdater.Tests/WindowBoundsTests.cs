using System.Windows;
using DLSSUpdater.Core;
using DLSSUpdater.Views;

namespace DLSSUpdater.Tests;

public sealed class WindowBoundsTests : IDisposable
{
    private static readonly Rect[] OneMonitor = [new(0, 0, 1920, 1040)];
    // A 4K monitor at 150% on the left, a 1080p one at 100% on the right whose top edge is lower.
    private static readonly Rect[] TwoMonitors = [new(-3840, 0, 3840, 2100), new(0, 300, 1920, 1040)];
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-bounds-" + Guid.NewGuid().ToString("N"));

    public WindowBoundsTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
    }

    public void Dispose() => Directory.Delete(_tmp, true);

    [Fact]
    public void OnScreen_RestoresTheSavedRect() =>
        Assert.Equal(new Rect(100, 50, 1400, 900), WindowBounds.Restore(100, 50, 1400, 900, OneMonitor));

    [Fact]
    public void NothingSaved_KeepsTheDefaults() =>
        Assert.Null(WindowBounds.Restore(null, null, null, null, OneMonitor));

    [Theory]
    [InlineData(3000, 100)]   // on a monitor that is gone
    [InlineData(100, -500)]   // title bar above the screen
    [InlineData(100, 1030)]   // title bar below the work area
    [InlineData(1850, 100)]   // only a sliver on the right edge
    public void OffScreen_KeepsTheDefaults(double left, double top) =>
        Assert.Null(WindowBounds.Restore(left, top, 1240, 800, OneMonitor));

    [Fact]
    public void PartlyOffScreen_WithTheTitleBarReachable_IsKept() =>
        Assert.NotNull(WindowBounds.Restore(-600, 200, 1240, 800, OneMonitor));

    [Fact]
    public void TitleBarInTheGapBetweenMonitors_KeepsTheDefaults() =>
        // Inside the virtual screen, but above the right monitor's top edge and right of the left one.
        Assert.Null(WindowBounds.Restore(200, 100, 1240, 800, TwoMonitors));

    [Fact]
    public void OnTheSecondMonitor_IsKept() =>
        Assert.Equal(new Rect(-3000, 200, 2400, 1500), WindowBounds.Restore(-3000, 200, 2400, 1500, TwoMonitors));

    [Fact]
    public void BiggerThanItsMonitor_IsShrunkToIt() =>
        // Saved at a size only the 4K monitor fits, with its title bar on the 1080p one.
        Assert.Equal(new Rect(100, 320, 1920, 1040), WindowBounds.Restore(100, 320, 3000, 2000, TwoMonitors));

    [Fact]
    public void TitleBarAcrossTwoMonitors_IsSizedForTheOneThatHoldsMostOfIt() =>
        Assert.Equal(new Rect(-400, 400, 1920, 1040), WindowBounds.Restore(-400, 400, 2600, 1400, TwoMonitors));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    public void Garbage_KeepsTheDefaults(double width) =>
        Assert.Null(WindowBounds.Restore(10, 10, width, 800, OneMonitor));

    [Theory]
    [InlineData(1, 0, false)]  // normal
    [InlineData(3, 0, true)]   // maximized
    [InlineData(2, 0, false)]  // minimized from normal
    [InlineData(2, 2, true)]   // minimized from maximized (WPF_RESTORETOMAXIMIZED)
    public void ClosedMinimized_KeepsWhetherItWasMaximized(int showCmd, int flags, bool expected) =>
        Assert.Equal(expected, WindowBounds.WasMaximized(showCmd, flags));

    [Fact]
    public void WindowBounds_SurviveARestart()
    {
        new AppSettings { WindowLeft = 10, WindowTop = 20, WindowWidth = 1300, WindowHeight = 850, WindowMaximized = true }.Save();
        var s = AppSettings.Load();
        Assert.Equal((10d, 20d, 1300d, 850d, true), (s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight, s.WindowMaximized));
    }
}
