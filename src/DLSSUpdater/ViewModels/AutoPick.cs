using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// One picker with an automatic choice: "Auto (X)" first (tag null), then the manual choices, and Reset to auto. The owner fills
/// <see cref="Choices"/> and hears about user changes through the callback; filling never raises it.
/// </summary>
public sealed partial class AutoPick(string label, string hint, Action<string?> picked) : ObservableObject
{
    private bool _filling;

    public string Label { get; } = label;
    public string Hint { get; } = hint;
    public ObservableCollection<DlssChoice> Choices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSet), nameof(StateText))]
    private DlssChoice? _selected;

    /// <summary>The user set a value (anything but the Auto entry).</summary>
    public bool IsSet => Selected?.Tag is not null;
    public string StateText => IsSet ? "set by you" : "auto";

    /// <summary>False hides the row (the Unreal version of a game that isn't Unreal, for example).</summary>
    [ObservableProperty] private bool _visible = true;

    /// <summary>Replaces the choices: <paramref name="auto"/> is the text inside "Auto (...)", <paramref name="current"/> the stored tag or null.</summary>
    public void Fill(string auto, IEnumerable<DlssChoice> manual, string? current)
    {
        _filling = true;
        Choices.Clear();
        Choices.Add(new DlssChoice(null, $"Auto ({auto})"));
        foreach (var c in manual) Choices.Add(c);
        if (current is not null && Choices.All(c => c.Tag != current)) Choices.Add(new DlssChoice(current, current));
        Selected = Choices.FirstOrDefault(c => c.Tag == current) ?? Choices[0];
        _filling = false;
    }

    partial void OnSelectedChanged(DlssChoice? value)
    {
        if (!_filling && value is not null) picked(value.Tag);
    }

    [RelayCommand]
    private void Reset() => Selected = Choices.FirstOrDefault();
}
