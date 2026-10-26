using System.Windows;

namespace DLSSUpdater.Views;

/// <summary>
/// Where the main window reopens: the normal rect it was closed at (Win32 window placement, physical pixels), unless that is no
/// longer on a monitor. Physical pixels keep the size right when the monitors have different scaling.
/// </summary>
public static class WindowBounds
{
    /// <summary>How much of the title bar must be on one monitor for a restored window, so the user can still grab it.</summary>
    public const double TitleBarHeight = 40;
    public const double MinVisibleWidth = 120;

    private const int SwShowMinimized = 2, SwShowMaximized = 3, WpfRestoreToMaximized = 2;

    /// <param name="workAreas">Each monitor's work area, in the coordinates of the saved rect. The gaps between monitors don't count.</param>
    /// <returns>The rect to restore, no bigger than the monitor that holds its title bar, or null to keep the default size and place.</returns>
    public static Rect? Restore(double? left, double? top, double? width, double? height, IReadOnlyList<Rect> workAreas)
    {
        if (left is not { } l || top is not { } t || width is not { } w || height is not { } h) return null;
        if (!double.IsFinite(l) || !double.IsFinite(t) || !double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0) return null;
        var titleBar = new Rect(l, t, w, TitleBarHeight);
        var monitor = workAreas
            .Select(area => (area, visible: Rect.Intersect(titleBar, area)))
            .Where(m => !m.visible.IsEmpty && m.visible.Width >= MinVisibleWidth && m.visible.Height >= TitleBarHeight / 2)
            .OrderByDescending(m => m.visible.Width * m.visible.Height)
            .Select(m => (Rect?)m.area)
            .FirstOrDefault();
        return monitor is { } area ? new Rect(l, t, Math.Min(w, area.Width), Math.Min(h, area.Height)) : null;
    }

    /// <summary>A window closed while minimized still counts as maximized when it would have restored to maximized.</summary>
    public static bool WasMaximized(int showCmd, int flags) =>
        showCmd == SwShowMaximized || showCmd == SwShowMinimized && (flags & WpfRestoreToMaximized) != 0;
}
