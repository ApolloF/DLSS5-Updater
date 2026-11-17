using CommunityToolkit.Mvvm.ComponentModel;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// The "HSF" section of a game's Install tab: what HSF needs here, what is installed and the Install / Remove state. HSF is import only
/// (private licence), so the section shows once a release is imported or installed. The long work runs through <see cref="MainViewModel"/>
/// so it shares the busy gate.
/// </summary>
public sealed partial class HsfViewModel(GameViewModel game, Services services) : ObservableObject
{
    private LibraryBuild? _build;

    public event Action? Changed;

    public bool Visible => services.Hsf is not null && !Demo.Active && (_build is not null || Installed is not null);

    private InstalledComponent? Installed => game.Manifest?.Component(ComponentRegistry.Hsf);

    public string StatusLine => (Installed, _build) switch
    {
        ({ } i, _) => $"Installed HSF {i.Version ?? "?"}, {(i.Extras.GetValueOrDefault("bits") ?? "64")}-bit add-on" +
                      (_build is { } b && b.Id != i.BuildId ? $" · {b.Label} is imported, press Reinstall to update" : ""),
        (null, { } b) => $"Not installed · HSF {b.Label} imported",
        _ => "",
    };

    public string BitsLine => game.Info.Effective.Is32Bit
        ? "32-bit game: the app places hsf.addon32 and the HSF_streamline folder."
        : game.Info.Effective.Bits == 0
            ? "Bitness unknown: the app places the 64-bit add-on only (hsf.addon64). Set the bitness under Detection if the game is 32-bit."
            : "64-bit game: the app places hsf.addon64 and the HSF_streamline folder.";

    public string NeedsText => "Needs: " + string.Join("; ", HsfNeeds.For(HsfNeeds.NeedsFlowSource(game.Info.Effective.PrimaryApi, NativeDlss), services.Gpu)) + ". " + HsfNeeds.AntiCheat + ".";

    private bool NativeDlss => Installer.HasNativeDlss(game.Info, game.Manifest, game.SelectedTarget?.Dir ?? game.Root);

    public string? Blocked => game.Info.AntiCheat is { } ac ? $"{ac} anti-cheat: HSF is never used in online games with anti-cheat, so it isn't installed here." : null;
    public bool HasBlocked => Blocked is not null;

    public bool CanInstall => _build is not null && Blocked is null && game.SelectedTarget is not null;
    public bool CanRemove => Installed is not null && game.SelectedTarget is not null;
    public string ActionText => Installed is null ? "Install" : "Reinstall";

    public void Refresh()
    {
        _build = services.Hsf is null ? null : HsfLibrary.Newest();
        OnPropertyChanged(string.Empty);
        Changed?.Invoke();
    }

    internal async Task InstallAsync(IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!CanInstall || game.SelectedTarget is not { } target || services.Hsf is not { } hsf)
            throw new InvalidOperationException(Blocked ?? "No HSF release is imported.");
        var over = services.Settings.Games.GetValueOrDefault(game.Id);
        var reshade = new HdrReShade(game.ReShadeChoice?.Tag, over?.Proxy, GameConfig.EffectiveReShade(services.Settings, game.Id), ReShadeIniTemplate.Load());
        await hsf.InstallAsync(game.Info, target.Dir, reshade, progress, ct);
        Log.Info($"{game.Name}: HSF installed");
    }

    internal async Task RemoveAsync(CancellationToken ct)
    {
        if (game.SelectedTarget is not { } target || services.Hsf is not { } hsf) return;
        await hsf.RemoveAsync(game.Info, target.Dir, ct);
        Log.Info($"{game.Name}: HSF removed");
    }
}
