namespace OrderTextTrainer.Core.Models;

public sealed class ProductMatchOverride
{
    public string MatchKey { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string? Note { get; set; }
}
