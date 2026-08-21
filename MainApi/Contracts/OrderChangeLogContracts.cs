namespace MainApi.Contracts;

public sealed class QueryOrderChangeLogsRequest : PagedQueryRequest
{
    public DateTime? ChangedAtStart { get; set; }

    public DateTime? ChangedAtEnd { get; set; }

    public string ReceiverName { get; set; } = string.Empty;

    public string ModifierLoginName { get; set; } = string.Empty;

    public string BusinessGroupName { get; set; } = string.Empty;

    public string OrderNo { get; set; } = string.Empty;
}

public sealed class OrderChangeLogResponse
{
    public long Id { get; set; }

    public string OrderNo { get; set; } = string.Empty;

    public string BusinessGroupName { get; set; } = string.Empty;

    public string ReceiverName { get; set; } = string.Empty;

    public string ModifierLoginName { get; set; } = string.Empty;

    public DateTime ChangedAtUtc { get; set; }

    public decimal PreviousAmount { get; set; }

    public decimal CurrentAmount { get; set; }

    public decimal AmountDifference { get; set; }

    public string ChangeSummary { get; set; } = string.Empty;
}
