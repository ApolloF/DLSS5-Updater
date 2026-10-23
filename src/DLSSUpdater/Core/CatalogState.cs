namespace DLSSUpdater.Core;

/// <summary>
/// The catalog as the app currently has it: the last good copy at startup, refreshed with each update check. Offline, the copy
/// stays and nothing throws. <see cref="Picker"/> is rebuilt only when the document changes.
/// </summary>
public sealed class CatalogState(CatalogClient client)
{
    public CatalogDocument? Document { get; private set; }
    public CatalogOrigin Origin { get; private set; } = CatalogOrigin.None;
    public string? Note { get; private set; }
    public HdrPicker? Picker { get; private set; }
    /// <summary>The Luma mods of the catalog; null when the catalog has no luma section.</summary>
    public LumaPicker? LumaPicker { get; private set; }

    public event Action? Changed;

    /// <summary>The copy on disk, with no request (startup).</summary>
    public void LoadCached() => Apply(client.LoadCached());

    public async Task RefreshAsync(CancellationToken ct) => Apply(await client.LoadAsync(ct));

    private void Apply(CatalogLoad load)
    {
        // A failed refresh never throws away a catalog we already have.
        var changed = false;
        if (load.Document is not null || Document is null)
        {
            changed = Document?.Generated != load.Document?.Generated || (Document is null) != (load.Document is null);
            Document = load.Document;
            Picker = load.Document?.Sections.Renodx is { } renodx ? new HdrPicker(renodx) : null;
            LumaPicker = load.Document?.Sections.Luma is { } luma ? new LumaPicker(luma) : null;
        }
        Origin = load.Origin;
        Note = load.Note;
        if (changed) Changed?.Invoke();
    }

    /// <summary>A line for the game page when the catalog can't be had: offline with no copy, or nothing yet.</summary>
    public string? Problem => Picker is not null ? null
        : Origin == CatalogOrigin.None ? "The mod catalog isn't available yet (offline and no earlier copy)." : "The catalog has no RenoDX data.";
}
