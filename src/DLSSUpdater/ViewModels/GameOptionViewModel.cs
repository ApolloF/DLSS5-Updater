using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// The per-game editor of one ini key. It shares the definition (label, choices, help) of the Settings option and
/// reads and writes the game's layer: "Inherit (…)" removes the key from the layer, a value overrides the base, and a
/// choice without a value ("Off", "Game setting") unsets a key the base sets.
/// </summary>
public sealed partial class GameOptionViewModel : ObservableObject
{
    /// <summary>
    /// The option tables of Settings, built once and shared by every game: only their label, choices and help are
    /// read here, never their (empty) override lists.
    /// </summary>
    public static class Definitions
    {
        public static readonly IReadOnlyList<IniOptionViewModel> ReShade = SettingsOptions.ReShade([]);
        public static readonly IReadOnlyList<IniOptionViewModel> Mfg = SettingsOptions.Mfg([]);
        public static readonly IReadOnlyList<IniOptionViewModel> RenoDx = SettingsOptions.RenoDx([]);
        public static readonly IReadOnlyList<IniOptionViewModel> Lecram = SettingsOptions.Lecram([]);
        public static readonly IReadOnlyList<IniOptionViewModel> Uplift = SettingsOptions.Uplift([]);
        public static readonly IReadOnlyList<IniOptionViewModel> Dfc = SettingsOptions.Dfc([]);
        public static readonly IReadOnlyList<IniOptionViewModel> Nr = SettingsOptions.Nr([]);
        public static readonly IReadOnlyList<IniOptionViewModel> Tonemap = SettingsOptions.Tonemap([]);
        public static readonly IReadOnlyList<IniOptionViewModel> HdrOutput = SettingsOptions.HdrOutput([]);
    }

    private readonly IniOptionViewModel _def;
    private readonly bool _reShade;
    private readonly AppSettings _s;
    private readonly string _gameId;
    private readonly Func<LecramGeneration?>? _generation;
    private OptionChoice _inherit = null!;

    /// <param name="generation">For Lecram's options: the generation of the build this game uses, which decides availability, hint and choices.</param>
    public GameOptionViewModel(IniOptionViewModel definition, bool reShade, AppSettings settings, string gameId, Func<LecramGeneration?>? generation = null)
    {
        _generation = generation;
        _def = definition;
        _reShade = reShade;
        _s = settings;
        _gameId = gameId;
        _def.PropertyChanged += (_, e) =>
        {
            if (_generation is not null) return;
            if (e.PropertyName == nameof(IniOptionViewModel.IsAvailable)) OnPropertyChanged(nameof(IsAvailable));
            else if (e.PropertyName == nameof(IniOptionViewModel.Hint)) OnPropertyChanged(nameof(Hint));
            else if (e.PropertyName == nameof(IniOptionViewModel.Choices)) Refresh();
        };
        Rebuild();
    }

    public bool IsAvailable => _generation is null ? _def.IsAvailable : _def.ViewFor(_generation()).IsAvailable;

    public string Label => _def.Label;
    public string Hint => _generation is null ? _def.Hint : _def.ViewFor(_generation()).Hint;
    private OptionChoice[] Choices => _generation is null ? _def.Choices : _def.ViewFor(_generation()).Choices;
    public string? Topic => _def.Topic;
    public string Tip => _def.Tip;

    public IReadOnlyList<OptionChoice> DisplayChoices { get; private set; } = [];

    private IReadOnlyList<IniOverride> Base => _reShade ? GameConfig.BaseReShade(_s, _gameId) : GameConfig.BaseOpti(_s, _gameId);
    private IniOverride? LayerEntry => GameConfig.LayerEntry(_s.Games.GetValueOrDefault(_gameId), _reShade, _def.Section, _def.Key);

    /// <summary>The key is set for this game (marker and reset button).</summary>
    public bool IsOverridden => LayerEntry is not null;

    private OptionChoice Match(string? value) =>
        Choices.FirstOrDefault(c => IniOptionViewModel.Same(c.Value, value))
        ?? (value is null ? Choices[0] : new OptionChoice($"{value} (custom)", value, "Set in the Advanced tab."));

    private string BaseLabel => Match(GameConfig.BaseEntry(Base, _def.Section, _def.Key)?.Value).Label;

    public OptionChoice Selected
    {
        get
        {
            if (LayerEntry is not { } e) return _inherit;
            return Match(e.Unset ? null : e.Value);
        }
        set
        {
            if (value is null) return;
            var g = _s.For(_gameId);
            if (ReferenceEquals(value, _inherit)) GameConfig.ClearLayer(g, _reShade, _def.Section, _def.Key);
            else GameConfig.SetLayer(g, _reShade, _def.Section, _def.Key, value.Value,
                GameConfig.BaseEntry(Base, _def.Section, _def.Key) is not null);
            _s.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOverridden));
        }
    }

    [RelayCommand]
    private void Reset() => Selected = _inherit;

    /// <summary>Re-reads the base (preset or settings changed) and the layer.</summary>
    public void Refresh()
    {
        Rebuild();
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(DisplayChoices));
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(IsOverridden));
    }

    private void Rebuild()
    {
        _inherit = new OptionChoice($"Inherit ({BaseLabel})", null, "Follows the base: the preset or current settings above.");
        var choices = new List<OptionChoice> { _inherit };
        choices.AddRange(Choices);
        if (LayerEntry is { Unset: false } e && Choices.All(c => !IniOptionViewModel.Same(c.Value, e.Value)))
            choices.Add(Match(e.Value));
        DisplayChoices = choices;
    }
}
