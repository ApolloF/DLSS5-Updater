using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DLSSUpdater.Core;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater;

/// <summary><c>--demo --screenshots &lt;folder&gt;</c>: renders the main views to PNG files and exits.</summary>
public partial class MainWindow
{
    private const double ShotWidth = 1280;
    private const double ShotHeight = 800;
    private static readonly int[] ShotScales = [1, 2];

    /// <summary>Gives the window the fixed size the screenshots are taken at.</summary>
    private void PrepareScreenshots()
    {
        Width = ShotWidth;
        Height = ShotHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Top = 0;
    }

    private async Task RunScreenshotExport(string folder)
    {
        var exitCode = 0;
        try { await ExportScreenshotsAsync(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error("Screenshot export failed", ex);
            exitCode = 1;
        }
        Application.Current.Shutdown(exitCode);
    }

    /// <summary>Writes NN-name.png at 1x and NN-name@2x.png at 2x (1280x800 and 2560x1600 pixels) for each view.</summary>
    internal async Task ExportScreenshotsAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var steps = new (string Name, Action Show)[]
        {
            ("01-games", () => ShowGame("Ember Crown", "Install")),
            ("02-games-updates", () => { ShowGame("Ember Crown", "Install"); _vm.Filter = "Updates"; }),
            ("03-game-options", () => ShowGame("Ember Crown", "Config")),
            ("04-game-addons", () => ShowGame("Neon Meridian", "Install")),
            ("05-game-addons-options", () => ShowGame("Neon Meridian", "Config")),
            ("06-game-anticheat", () => ShowGame("Iron Veil", "Install")),
            ("07-settings-general", () => ShowSettings("General")),
            ("08-settings-keybinds", () => ShowSettings("Keybinds")),
            ("09-settings-reshade", () => ShowSettings("ReShade")),
            ("10-settings-nr", () => ShowSettings("Nr")),
            ("11-settings-advanced", () => ShowSettings("Advanced")),
            ("12-log", () => { ShowGame("Ember Crown", "Install"); _vm.LogOpen = true; }),
            ("13-about", () => _vm.AboutOpen = true),
        };
        foreach (var (name, show) in steps)
        {
            ResetView();
            show();
            // Deferred pages realise and bindings settle over a few dispatcher passes.
            await Task.Delay(800);
            UpdateLayout();
            foreach (var scale in ShotScales) Save(Path.Combine(folder, name + (scale == 1 ? "" : $"@{scale}x") + ".png"), scale);
        }
        Log.Info($"Wrote {steps.Length * ShotScales.Length} screenshots to {folder}");
    }

    private void ResetView()
    {
        _vm.SettingsOpen = false;
        _vm.AboutOpen = false;
        _vm.LogOpen = false;
        _vm.Search = "";
        _vm.Filter = "All";
    }

    private void ShowGame(string name, string tab)
    {
        var game = _vm.Games.First(g => g.Name == name);
        game.DetailTab = tab;
        _vm.SelectedGame = game;
        _vm.ShowGameView();
    }

    private void ShowSettings(string tab)
    {
        _vm.SettingsOpen = true;
        _vm.SettingsVm.Tab = tab;
    }

    /// <summary>Renders the window at a fixed size regardless of the screen's scaling, so files are the same everywhere.</summary>
    private void Save(string path, int scale)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(RootGrid) { Stretch = Stretch.Fill }, null, new Rect(0, 0, ShotWidth, ShotHeight));
        var bmp = new RenderTargetBitmap((int)(ShotWidth * scale), (int)(ShotHeight * scale), 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
