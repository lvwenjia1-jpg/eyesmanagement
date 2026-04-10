using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class ReplaceProductCatalogRequest
{
    public string SourceFileName { get; set; } = string.Empty;

    [MinLength(1)]
    public List<ProductCatalogEntryRequest> Entries { get; set; } = new();
}

public sealed class ProductCatalogEntryRequest
{
    [Required]
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string SpecCode { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

    public string BaseName { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;

    public string SearchText { get; set; } = string.Empty;
}

public sealed class ProductCatalogSyncResponse
{
    public int EntryCount { get; set; }

    public string UpdatedByLoginName { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}
