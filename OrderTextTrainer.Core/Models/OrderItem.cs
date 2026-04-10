using System.Text.Json.Serialization;

namespace OrderTextTrainer.Core.Models;

public sealed class OrderItem
{
    public string RawText { get; set; } = string.Empty;

    public string? ProductName { get; set; }

    public string? LeftPower { get; set; }

    public string? RightPower { get; set; }

    public string? PowerSummary { get; set; }

    public int? Quantity { get; set; }

    public bool IsTrial { get; set; }

    public bool IsOutOfStock { get; set; }

    public string MatchSource { get; set; } = string.Empty;

    public string? ResolvedGoodsCode { get; set; }

    public string? ResolvedGoodsName { get; set; }

    public string? ResolvedSpecCode { get; set; }

    public string? ResolvedBarcode { get; set; }

    public string? MatchNote { get; set; }

    [JsonIgnore]
    public List<ProductCatalogEntry> MatchCandidates { get; set; } = new();
}
