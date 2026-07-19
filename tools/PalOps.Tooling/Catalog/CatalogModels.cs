namespace PalOps.Tooling.Catalog;

public sealed class CatalogEntryModel
{
    public string Type { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string NameZh { get; init; } = string.Empty;
    public string NameEn { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string ImageUrl { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}
