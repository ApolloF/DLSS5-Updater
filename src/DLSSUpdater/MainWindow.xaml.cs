using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DLSSUpdater.Core;
using DLSSUpdater.ViewModels;
using DLSSUpdater.Views;

namespace DLSSUpdater;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private double _settingsOffset;
    // Inside the deferred settings and about pages; set once each page is first shown.
    private ScrollViewer? _settingsScroll;
    private ScrollViewer? _aboutScroll;
    private ItemsControl? _guideList;

    public MainWindow()
    {
        InitializeComponent();
        if (Demo.ScreenshotDir is not null) PrepareScreenshots();
        _vm = new MainViewModel();
        DataContext = _vm;

        Loaded += async (_, _) =>
        {
            if (Demo.ScreenshotDir is null) _vm.SettingsVm.ShowOnboardingIfDue();
            await _vm.InitializeAsync();
            if (Demo.Active && Demo.ScreenshotDir is { } shots) await RunScreenshotExport(shots);
            else if (Environment.GetEnvironmentVariable("DLSSU_SNAPSHOT") is { Length: > 0 } shot) await Snapshot(shot);
        };
        StateChanged += (_, _) => UpdateMaximized();
        Closed += (_, _) => Core.HashCache.Save();
        PreviewKeyDown += OnPreviewKeyDown;
        KeyDown += OnKeyDown;
        SettingsHost.Realized += root => _settingsScroll = Deferred.Find<ScrollViewer>(root, "SettingsScroll");
        AboutHost.Realized += root =>
        {
            _aboutScroll = Deferred.Find<ScrollViewer>(root, "AboutScroll");
            _guideList = Deferred.Find<ItemsControl>(root, "GuideList");
        };
        _vm.LeavingSettings += () => _settingsOffset = _settingsScroll?.VerticalOffset ?? 0;
        _vm.ReturnedToSettings += () => Dispatcher.BeginInvoke(
            () => _settingsScroll?.ScrollToVerticalOffset(_settingsOffset), System.Windows.Threading.DispatcherPriority.Loaded);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.HelpTarget) && _vm.HelpTarget is { } id)
                Dispatcher.BeginInvoke(() => ScrollToGuide(id), System.Windows.Threading.DispatcherPriority.Loaded);
        };
        SourceInitialized += (_, _) => RoundCorners();
        ListAutoScroll.FollowNewest(LogList, _vm.LogLines);
    }

    /// <summary>While a keybind button is waiting, the next key press becomes the binding.</summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_vm.SettingsVm.Capturing is null) return;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        var mods = System.Windows.Input.Keyboard.Modifiers;
        e.Handled = _vm.SettingsVm.HandleKey(vk,
            mods.HasFlag(System.Windows.Input.ModifierKeys.Control),
            mods.HasFlag(System.Windows.Input.ModifierKeys.Shift),
            mods.HasFlag(System.Windows.Input.ModifierKeys.Alt));
    }

    /// <summary>Esc closes Settings or About. It runs after the controls, so an open dropdown or a keybind capture gets Esc first.</summary>
    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Handled || e.Key != System.Windows.Input.Key.Escape || !(_vm.SettingsOpen || _vm.AboutOpen)) return;
        if (_vm.AboutOpen && _vm.CanGoBack) _vm.BackCommand.Execute(null);
        else _vm.ShowGameView();
        e.Handled = true;
    }

    private static readonly System.Windows.Input.Key[] ListKeys =
    [
        System.Windows.Input.Key.Up, System.Windows.Input.Key.Down, System.Windows.Input.Key.PageUp, System.Windows.Input.Key.PageDown,
        System.Windows.Input.Key.Home, System.Windows.Input.Key.End, System.Windows.Input.Key.Enter, System.Windows.Input.Key.Space,
    ];

    /// <summary>Picking a game with the keyboard leaves Settings / About, as a click does.</summary>
    private void OnGameListKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ListKeys.Contains(e.Key)) _vm.ShowGameView();
    }

    private void OnGameListClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement((ItemsControl)sender, d) is ListBoxItem)
            _vm.ShowGameView();
    }

    /// <summary>Scrolls the About page to a guide entry and marks it with the accent bar.</summary>
    private void ScrollToGuide(string id)
    {
        if (_guideList is null || _aboutScroll is null) return;
        _guideList.UpdateLayout();
        foreach (var entry in _vm.Guide)
        {
            var topic = entry.Topic;
            if (_guideList.ItemContainerGenerator.ContainerFromItem(entry) is not FrameworkElement c) continue;
            var border = VisualTreeHelper.GetChildrenCount(c) > 0 ? VisualTreeHelper.GetChild(c, 0) as System.Windows.Controls.Border : null;
            if (border is not null)
                border.BorderBrush = topic.Id == id ? (Brush)FindResource("Accent") : Brushes.Transparent;
            if (topic.Id == id && _aboutScroll.Content is UIElement content)
                _aboutScroll.ScrollToVerticalOffset(Math.Max(0, c.TranslatePoint(new Point(0, 0), content).Y - 24));
        }
    }

    private void UpdateMaximized()
    {
        var maximized = WindowState == WindowState.Maximized;
        RootGrid.Margin = maximized ? MaximizedOverhang() : new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetName(MaxButton, maximized ? "Restore" : "Maximize");
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    /// <summary>
    /// A chrome-less maximized window overhangs the screen by the sizing frame plus the padded border. SystemParameters
    /// may leave the padded border out, which would clip a few pixels, so both metrics are read for this window's DPI.
    /// </summary>
    private Thickness MaximizedOverhang()
    {
        var scale = VisualTreeHelper.GetDpi(this);
        var dpi = (uint)Math.Round(scale.PixelsPerInchX);
        var x = (GetSystemMetricsForDpi(SmCxSizeFrame, dpi) + GetSystemMetricsForDpi(SmCxPaddedBorder, dpi)) / scale.DpiScaleX;
        var y = (GetSystemMetricsForDpi(SmCySizeFrame, dpi) + GetSystemMetricsForDpi(SmCxPaddedBorder, dpi)) / scale.DpiScaleY;
        return new Thickness(x, y, x, y);
    }

    private const int SmCxSizeFrame = 32, SmCySizeFrame = 33, SmCxPaddedBorder = 92;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Dev aid: DLSSU_SNAPSHOT=file.png renders the window to disk (DLSSU_SNAPSHOT_STEPS=settings,tab:X,scroll:Y,log,game:N,name:Text,detail:Tab,mod:Label).</summary>
    private async Task Snapshot(string path)
    {
        var steps = (Environment.GetEnvironmentVariable("DLSSU_SNAPSHOT_STEPS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var step in steps)
        {
            if (step == "settings") _vm.SettingsOpen = true;
            else if (step.StartsWith("tab:")) _vm.SettingsVm.Tab = step[4..];
            else if (step.StartsWith("scroll:") && double.TryParse(step[7..], out var y))
            {
                await Task.Delay(400);
                _settingsScroll?.ScrollToVerticalOffset(y);
            }
            else if (step == "about") _vm.AboutOpen = true;
            else if (step.StartsWith("help:")) _vm.ShowHelpCommand.Execute(step[5..]);
            else if (step == "nvgame" && _vm.SelectedGame is { } g) g.DetailTab = "Nvidia";
            else if (step.StartsWith("name:"))
                _vm.SelectedGame = _vm.GamesView.Cast<GameViewModel>().FirstOrDefault(g => g.Name.Contains(step[5..], StringComparison.OrdinalIgnoreCase));
            else if (step.StartsWith("detail:") && _vm.SelectedGame is { } dg) dg.DetailTab = step[7..];
            else if (step.StartsWith("mod:"))
            {
                var label = step[4..];
                if (_vm.SettingsOpen) _vm.SettingsVm.ModSelection = _vm.SettingsVm.ModChoices.First(c => c.Label.Contains(label, StringComparison.OrdinalIgnoreCase));
                else if (_vm.SelectedGame is { } mg) mg.ModSelection = mg.ModChoices.First(c => c.Label.Contains(label, StringComparison.OrdinalIgnoreCase));
            }
            else if (step == "log") _vm.LogOpen = true;
            else if (step.StartsWith("game:") && int.TryParse(step[5..], out var i))
                _vm.SelectedGame = _vm.GamesView.Cast<GameViewModel>().ElementAtOrDefault(i);
        }
        await Task.Delay(600);
        var dpi = VisualTreeHelper.GetDpi(this);
        var bmp = new RenderTargetBitmap((int)(RootGrid.ActualWidth * dpi.DpiScaleX), (int)(RootGrid.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(RootGrid);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        await using (var fs = File.Create(path)) enc.Save(fs);
        if (Environment.GetEnvironmentVariable("DLSSU_SNAPSHOT_EXIT") == "1") Close();
    }

    private void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var round = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE (Win11 only)
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
