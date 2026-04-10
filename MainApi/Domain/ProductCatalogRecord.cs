namespace MainApi.Domain;

public sealed class ProductCatalogEntryRecord
{
    public long Id { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string SpecCode { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

    public string BaseName { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;

    public string SearchText { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
