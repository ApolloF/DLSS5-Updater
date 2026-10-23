using CommunityToolkit.Mvvm.ComponentModel;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>One add-on or shader pack in a list: what it is, where it comes from, and whether it is ticked.</summary>
public sealed partial class ExtrasItemViewModel : ObservableObject
{
    private readonly Action<ExtrasItemViewModel>? _changed;
    private bool _silent;

    public ExtrasItemViewModel(string id, string name, string? description, string detail, string? note, bool available, bool imported,
        string category, bool selected, Action<ExtrasItemViewModel>? changed)
    {
        Id = id;
        Name = name;
        Description = description;
        Detail = detail;
        Note = note;
        Available = available;
        Imported = imported;
        Category = category;
        _selected = selected;
        _changed = changed;
    }

    public string Id { get; }
    public string Name { get; }
    public string? Description { get; }
    /// <summary>"Author · MIT · mirrored" and the like.</summary>
    public string Detail { get; }
    /// <summary>Why it can't be ticked, or a remark (managed elsewhere, no download).</summary>
    public string? Note { get; }
    public bool HasNote => Note is not null;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    /// <summary>False when the app can't install it (managed as a component of its own, or no verified download).</summary>
    public bool Available { get; }
    public bool Imported { get; }
    public string Category { get; }

    [ObservableProperty] private bool _selected;

    /// <summary>Installed in the game this row belongs to (game page only).</summary>
    [ObservableProperty] private bool _installed;

    partial void OnSelectedChanged(bool value)
    {
        if (!_silent) _changed?.Invoke(this);
    }

    /// <summary>Sets the tick without telling anyone (the list is being refreshed).</summary>
    public void SetSelected(bool value)
    {
        _silent = true;
        Selected = value;
        _silent = false;
    }

    internal static string AddonDetail(CatalogAddon a) =>
        string.Join(" · ", new[] { a.Imported ? "imported by you" : a.Author, a.License ?? (a.Imported ? null : "no licence listed"), SourceText(a.IsMirrored, a.Imported, a.Files.Count > 0) }.Where(s => !string.IsNullOrEmpty(s)));

    internal static string PackDetail(CatalogShaderPack p) =>
        string.Join(" · ", new[]
        {
            p.Imported ? "imported by you" : CategoryLabel(p.Category), p.License ?? (p.Imported ? null : "no licence listed"), SourceText(p.IsMirrored, p.Imported, p.File is not null),
            p.Layout is { Effects.Count: > 0 } l ? $"{l.Effects.Count} effects" : null,
        }.Where(s => !string.IsNullOrEmpty(s)));

    private static string? SourceText(bool mirrored, bool imported, bool hasFile) =>
        imported ? null : !hasFile ? null : mirrored ? "mirrored copy" : "pinned to the author's file";

    public static string CategoryLabel(string category) => category switch
    {
        "hdr" => "HDR",
        "general" => "General",
        "color" => "Color",
        "depth-3d" => "Depth & 3D",
        "cinematic" => "Cinematic",
        _ => category,
    };

    /// <summary>Why the add-on can't be ticked; for <paramref name="bits"/> null (no game), only when neither a 32-bit nor a 64-bit game could use it.</summary>
    internal static string? AddonNote(CatalogAddon a, int? bits)
    {
        var why = bits is { } b ? ReShadeAddonPlanner.WhyNot(a, b)
            : ReShadeAddonPlanner.WhyNot(a, 64) is { } w64 && ReShadeAddonPlanner.WhyNot(a, 32) is not null ? w64 : null;
        return why is null ? null : char.ToUpperInvariant(why[0]) + why[1..];
    }
}
