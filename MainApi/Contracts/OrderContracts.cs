namespace MainApi.Contracts;

public sealed class QueryBusinessGroupOrdersRequest : PagedQueryRequest
{
    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }
}

public sealed class DashboardOrderItemResponse
{
    public long Id { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

public class DashboardOrderSummaryResponse
{
    public long Id { get; set; }

    public string OrderNo { get; set; } = string.Empty;

    public string UploaderLoginName { get; set; } = string.Empty;

    public string ReceiverName { get; set; } = string.Empty;

    public string ReceiverAddress { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string TrackingNumber { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public IReadOnlyList<DashboardOrderItemResponse> Items { get; set; } = Array.Empty<DashboardOrderItemResponse>();
}

public sealed class DashboardOrderDetailResponse : DashboardOrderSummaryResponse
{
    public long BusinessGroupId { get; set; }

    public string BusinessGroupName { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class UpdateDashboardOrderRequest
{
    public decimal Amount { get; set; }

    public string TrackingNumber { get; set; } = string.Empty;
}
