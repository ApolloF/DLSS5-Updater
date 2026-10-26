using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DLSSUpdater.Views;

/// <summary>
/// The mouse wheel over a closed ComboBox scrolls the page instead of changing the value: scrolling through a settings
/// page shouldn't change what it passes over. With the list open the wheel scrolls the list as usual.
/// </summary>
public static class WheelGuard
{
    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(ComboBox), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox { IsDropDownOpen: false } box || e.Handled) return;
        e.Handled = true;
        if (VisualTreeHelper.GetParent(box) is UIElement parent)
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = box });
    }
}
