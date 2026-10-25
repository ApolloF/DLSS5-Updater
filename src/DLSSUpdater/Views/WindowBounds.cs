using System.Windows;

namespace DLSSUpdater.Views;

/// <summary>Where the main window reopens: the size and place it was closed at, unless that is no longer on the screen.</summary>
public static class WindowBounds
{
    /// <summary>How much of the title bar must be on the screen for a restored window, so the user can still grab it.</summary>
    public const double TitleBarHeight = 40;
    public const double MinVisibleWidth = 120;

    /// <param name="screen">The desktop in the window's units (WPF: the virtual screen).</param>
    /// <param name="min">The window's minimum size; a smaller saved size grows to it.</param>
    /// <returns>The rect to restore, or null to keep the default size and place.</returns>
    public static Rect? Restore(double? left, double? top, double? width, double? height, Rect screen, Size min)
    {
        if (left is not { } l || top is not { } t || width is not { } w || height is not { } h) return null;
        if (!double.IsFinite(l) || !double.IsFinite(t) || !double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0) return null;
        var rect = new Rect(l, t, Math.Max(w, min.Width), Math.Max(h, min.Height));
        var visible = Rect.Intersect(new Rect(rect.X, rect.Y, rect.Width, TitleBarHeight), screen);
        return !visible.IsEmpty && visible.Width >= MinVisibleWidth && visible.Height >= TitleBarHeight / 2 ? rect : null;
    }
}
