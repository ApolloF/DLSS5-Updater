namespace DLSSUpdater.Core;

/// <summary>What the method advisor reads from the catalog, built once per catalog document (the document is replaced as a whole on refresh).</summary>
public sealed class AdvisorCatalog
{
    public static readonly AdvisorCatalog Empty = new(null);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CatalogDocument, AdvisorCatalog> Cache = [];

    public OptiCompatIndex OptiCompat { get; }
    /// <summary>Matches games to RenoDX's list; null when the catalog has no RenoDX section.</summary>
    public GameMatcher? RenoDx { get; }

    private AdvisorCatalog(CatalogDocument? doc)
    {
        OptiCompat = new OptiCompatIndex(doc?.Sections.OptiscalerCompat);
        RenoDx = doc?.Sections.Renodx is { } renodx ? new GameMatcher(renodx) : null;
    }

    public static AdvisorCatalog For(CatalogDocument? doc) => doc is null ? Empty : Cache.GetValue(doc, d => new AdvisorCatalog(d));
}
