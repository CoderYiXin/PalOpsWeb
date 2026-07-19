namespace PalOps.Web.Catalog;

public sealed record CatalogEntry(
    string Type,
    string Id,
    string NameZh,
    string NameEn,
    string Category,
    IReadOnlyList<string> Aliases,
    string ImageUrl = "",
    bool Favorite = false,
    DateTimeOffset? LastUsedAt = null,
    string Source = "seed");

public sealed record CatalogSearchResult(IReadOnlyList<CatalogEntry> Entries, int Total);
public sealed record CatalogCategoryCount(string Category, int Count);
public sealed record CatalogImportResult(int Imported, int Replaced, int Rejected, IReadOnlyList<string> Errors);
