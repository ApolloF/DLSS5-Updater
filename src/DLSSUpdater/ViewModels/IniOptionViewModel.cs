using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>A labelled value; Value null means "leave the key alone" (no override).</summary>
public sealed record OptionChoice(string Label, string? Value, string? Description = null)
{
    public override string ToString() => Label;
}

/// <summary>
/// A friendly editor for one ini key, backed by an override list. Toggles use Choices[0] = off, Choices[1] = on.
/// </summary>
public sealed partial class IniOptionViewModel : ObservableObject
{
    private readonly ObservableCollection<IniOverride> _list;

    public IniOptionViewModel(string label, string hint, string section, string key, ObservableCollection<IniOverride> list, params OptionChoice[] choices)
    {
        Label = label;
        Hint = hint;
        Section = section;
        Key = key;
        _list = list;
        Choices = _baseChoices = choices;
        _baseHint = hint;
        Hint = hint;
    }

    public static IniOptionViewModel Toggle(string label, string hint, string section, string key, string on, ObservableCollection<IniOverride> list) =>
        new(label, hint, section, key, list, new OptionChoice("Off", null), new OptionChoice("On", on));

    /// <summary>HelpTopics id behind the ⓘ button.</summary>
    public string? Topic { get; private set; }
    public string Tip => HelpTopics.Tip(Topic);

    public IniOptionViewModel About(string topic)
    {
        Topic = topic;
        return this;
    }

    public string Label { get; }

    /// <summary>False when the chosen OptiScaler build has no such key; the row is hidden.</summary>
    [ObservableProperty] private bool _isAvailable = true;
    [ObservableProperty] private string _hint = "";
    public string Section { get; }
    public string Key { get; }
    public OptionChoice[] Choices { get; private set; }

    private readonly string _baseHint;
    private readonly OptionChoice[] _baseChoices;
    private readonly Dictionary<OptiSource, (string? Hint, OptionChoice[]? Choices)> _variants = [];

    /// <summary>The key means something else in <paramref name="source"/>: other values, defaults or advice.</summary>
    public IniOptionViewModel In(OptiSource source, string? hint = null, params OptionChoice[] choices)
    {
        _variants[source] = (hint, choices.Length > 0 ? choices : null);
        return this;
    }

    /// <summary>Switches hint and choices to the build's variant and hides the row when the build behind <paramref name="tag"/> lacks the key.</summary>
    public void ApplySource(OptiSource source, string? tag = null)
    {
        IsAvailable = !source.Lacks(Section, Key, tag);
        var v = _variants.GetValueOrDefault(source);
        Show(v.Hint, v.Choices);
    }

    private readonly Dictionary<LecramGeneration, (string? Hint, OptionChoice[]? Choices)> _generationVariants = [];

    /// <summary>The key means something else in Lecram's <paramref name="generation"/>: other values, defaults or advice.</summary>
    public IniOptionViewModel In(LecramGeneration generation, string? hint = null, params OptionChoice[] choices)
    {
        _generationVariants[generation] = (hint, choices.Length > 0 ? choices : null);
        return this;
    }

    /// <summary>
    /// Switches to the values of the default Lecram build's generation and hides the row when that build lacks the key.
    /// No build (null) shows every option with the choices that fit both.
    /// </summary>
    /// <param name="build">The build's detected label: v11 keys newer than it are hidden.</param>
    public void ApplyGeneration(LecramGeneration? generation, string? build = null)
    {
        var view = ViewFor(generation, build);
        IsAvailable = view.IsAvailable;
        Show(view.Hint, view.Choices);
    }

    /// <summary>What this option offers for <paramref name="generation"/>, without touching the shared state: a game uses the generation of its own build.</summary>
    public (bool IsAvailable, string Hint, OptionChoice[] Choices) ViewFor(LecramGeneration? generation, string? build = null)
    {
        var v = generation is { } g ? _generationVariants.GetValueOrDefault(g) : default;
        return (LecramIni.IsAvailable(Key, generation, build), v.Hint ?? _baseHint, v.Choices ?? _baseChoices);
    }

    private void Show(string? hint, OptionChoice[]? variantChoices)
    {
        Hint = hint ?? _baseHint;
        var choices = variantChoices ?? _baseChoices;
        if (ReferenceEquals(choices, Choices)) return;
        Choices = choices;
        OnPropertyChanged(nameof(Choices));
        Refresh();
    }
    public bool IsToggle => Choices.Length == 2 && Choices[0].Value is null && Choices[1].Label == "On";

    /// <summary>Choices plus the current value when it isn't one of them (e.g. typed in Advanced).</summary>
    public IReadOnlyList<OptionChoice> DisplayChoices
    {
        get
        {
            var sel = Selected;
            return Choices.Contains(sel) ? Choices : [.. Choices, sel];
        }
    }

    private IniOverride? Entry => _list.FirstOrDefault(o =>
        o.Section.Equals(Section, StringComparison.OrdinalIgnoreCase) && o.Key.Equals(Key, StringComparison.OrdinalIgnoreCase));

    public OptionChoice Selected
    {
        get
        {
            var v = Entry?.Value;
            return Choices.FirstOrDefault(c => Same(c.Value, v))
                   ?? (v is null ? Choices[0] : new OptionChoice($"{v} (custom)", v, "Set in the Advanced tab."));
        }
        set
        {
            if (value is null) return;
            var e = Entry;
            if (value.Value is null)
            {
                if (e is not null) _list.Remove(e);
            }
            else if (e is null) _list.Add(new IniOverride(Section, Key, value.Value));
            else _list[_list.IndexOf(e)] = new IniOverride(Section, Key, value.Value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOn));
        }
    }

    /// <summary>Numbers compare by value so "0.7" matches "0.700000" as OptiScaler writes it.</summary>
    internal static bool Same(string? a, string? b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        return a is not null && b is not null
               && double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
               && double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
               && Math.Abs(x - y) < 1e-6;
    }

    public bool IsOn
    {
        get => Selected.Value is not null;
        set => Selected = value ? Choices[^1] : Choices[0];
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(DisplayChoices));
    }
}
