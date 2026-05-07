using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class QueryProductCatalogRequest : PagedQueryRequest
{
    public string Keyword { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;
}

public sealed class CreateProductCatalogRequest
{
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string SpecCode { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

    public bool IsOutOfStock { get; set; }
}

public static class ProductCatalogImportModes
{
    public const string Incremental = "incremental";
    public const string Overwrite = "overwrite";
    public const string StockOut = "stock_out";
    public const string StockIn = "stock_in";
}

public sealed class ImportProductCatalogRequest
{
    public string SourceFileName { get; set; } = string.Empty;

    public string ImportMode { get; set; } = ProductCatalogImportModes.Incremental;

    [MinLength(1)]
    public List<ImportProductCatalogItemRequest> Entries { get; set; } = new();
}

public sealed class ImportProductCatalogItemRequest
{
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string SpecCode { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

    public bool IsOutOfStock { get; set; }
}

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

    public bool IsOutOfStock { get; set; }

    public string SearchText { get; set; } = string.Empty;
}

public sealed class UpdateProductCatalogOutOfStockRequest
{
    public bool IsOutOfStock { get; set; }
}

public sealed class UpdateProductCatalogGroupSpecificationRequest
{
    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string TargetSpecificationToken { get; set; } = string.Empty;
}

public sealed class ProductCatalogSyncResponse
{
    public int EntryCount { get; set; }

    public string UpdatedByLoginName { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ProductCatalogImportResponse
{
    public int AddedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int SkippedCount { get; set; }

    public int TotalCount { get; set; }

    public string SourceFileName { get; set; } = string.Empty;

    public string ImportMode { get; set; } = ProductCatalogImportModes.Incremental;

    public DateTime UpdatedAtUtc { get; set; }

    public string Message { get; set; } = string.Empty;
}

public sealed class ProductCatalogGroupResponse
{
    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public int ItemCount { get; set; }

    public int DegreeCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public List<ProductCatalogDegreeResponse> Degrees { get; set; } = new();
}

public sealed class ProductCatalogDegreeResponse
{
    public long Id { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string SpecCode { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;

    public bool IsOutOfStock { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
