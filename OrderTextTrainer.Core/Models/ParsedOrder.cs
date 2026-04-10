namespace OrderTextTrainer.Core.Models;

public sealed class ParsedOrder
{
    public string SourceText { get; set; } = string.Empty;

    public string? CustomerName { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? Brand { get; set; }

    public string? DetectedWearPeriod { get; set; }

    public string? WearPeriod { get; set; }

    public string WearPeriodMatchSource { get; set; } = string.Empty;

    public string? WearPeriodMatchNote { get; set; }

    public string? Remark { get; set; }

    public List<OrderItem> Items { get; set; } = new();

    public List<string> Gifts { get; set; } = new();

    public List<string> OutOfStockLines { get; set; } = new();
}
