namespace MainApi.Domain;

public sealed class OrderChangeLogQuery
{
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public DateTime? ChangedAtStartUtc { get; set; }

    public DateTime? ChangedAtEndUtc { get; set; }

    public string ReceiverName { get; set; } = string.Empty;

    public string ModifierLoginName { get; set; } = string.Empty;

    public string BusinessGroupName { get; set; } = string.Empty;

    public string OrderNo { get; set; } = string.Empty;
}

public sealed class OrderChangeLogRecord
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
