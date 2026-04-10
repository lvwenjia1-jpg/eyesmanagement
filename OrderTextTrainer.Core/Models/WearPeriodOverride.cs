namespace OrderTextTrainer.Core.Models;

public sealed class WearPeriodOverride
{
    public string OrderKey { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;

    public string? Note { get; set; }
}
