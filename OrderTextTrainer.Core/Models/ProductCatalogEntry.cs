namespace OrderTextTrainer.Core.Models;

public sealed class ProductCatalogEntry
{
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string SpecCode { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

    public string BaseName { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;

    public string SearchText { get; set; } = string.Empty;

    public string DisplayText => string.IsNullOrWhiteSpace(Barcode)
        ? $"{ProductCode} | {ProductName}"
        : $"{ProductCode} | {ProductName} | {Barcode}";
}
