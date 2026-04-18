namespace MainApi.Domain;

public sealed class DashboardOrderQuery
{
    public long BusinessGroupId { get; set; }

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public DateTime? StartTimeUtc { get; set; }

    public DateTime? EndTimeUtc { get; set; }
}

public class DashboardOrderSummaryRecord
{
    public long Id { get; set; }

    public string OrderNo { get; set; } = string.Empty;

    public string UploaderLoginName { get; set; } = string.Empty;

    public string ReceiverName { get; set; } = string.Empty;

    public string ReceiverAddress { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string TrackingNumber { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public IReadOnlyList<DashboardOrderItemRecord> Items { get; set; } = Array.Empty<DashboardOrderItemRecord>();
}

public sealed class DashboardOrderDetailRecord : DashboardOrderSummaryRecord
{
    public long BusinessGroupId { get; set; }

    public string BusinessGroupName { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class DashboardOrderItemRecord
{
    public long Id { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
