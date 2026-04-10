namespace MainApi.Domain;

public sealed class UploadCreateCommand
{
    public string DraftId { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string UploaderLoginName { get; set; } = string.Empty;

    public string UploaderDisplayName { get; set; } = string.Empty;

    public string UploaderErpId { get; set; } = string.Empty;

    public string UploaderWecomId { get; set; } = string.Empty;

    public string MachineCode { get; set; } = string.Empty;

    public string ReceiverName { get; set; } = string.Empty;

    public string ReceiverMobile { get; set; } = string.Empty;

    public string ReceiverAddress { get; set; } = string.Empty;

    public string Remark { get; set; } = string.Empty;

    public bool HasGift { get; set; }

    public string Status { get; set; } = string.Empty;

    public string StatusDetail { get; set; } = string.Empty;

    public string ExternalRequestJson { get; set; } = string.Empty;

    public string ExternalResponseJson { get; set; } = string.Empty;

    public List<UploadItemCommand> Items { get; set; } = new();
}

public sealed class UploadListQuery
{
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 100;

    public int? CreatedOn { get; set; }

    public int? CreatedOnFrom { get; set; }

    public int? CreatedOnTo { get; set; }

    public string MachineCode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string UploaderLoginName { get; set; } = string.Empty;
}

public sealed class UploadListResult
{
    public int TotalCount { get; set; }

    public int PageNumber { get; set; }

    public int PageSize { get; set; }

    public IReadOnlyList<UploadSummaryRecord> Items { get; set; } = Array.Empty<UploadSummaryRecord>();
}

public sealed class UploadItemCommand
{
    public string SourceText { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public string DegreeText { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;

    public string Remark { get; set; } = string.Empty;

    public bool IsTrial { get; set; }
}

public class UploadSummaryRecord
{
    public long Id { get; set; }

    public string UploadNo { get; set; } = string.Empty;

    public string DraftId { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string UploaderLoginName { get; set; } = string.Empty;

    public string UploaderDisplayName { get; set; } = string.Empty;

    public string UploaderErpId { get; set; } = string.Empty;

    public string UploaderWecomId { get; set; } = string.Empty;

    public string MachineCode { get; set; } = string.Empty;

    public string ReceiverName { get; set; } = string.Empty;

    public string ReceiverMobile { get; set; } = string.Empty;

    public string ReceiverAddress { get; set; } = string.Empty;

    public bool HasGift { get; set; }

    public string Status { get; set; } = string.Empty;

    public string StatusDetail { get; set; } = string.Empty;

    public int ItemCount { get; set; }

    public int CreatedOn { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public sealed class UploadDetailRecord : UploadSummaryRecord
{
    public string Remark { get; set; } = string.Empty;

    public string ExternalRequestJson { get; set; } = string.Empty;

    public string ExternalResponseJson { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }

    public IReadOnlyList<UploadItemRecord> Items { get; set; } = Array.Empty<UploadItemRecord>();
}

public sealed class UploadItemRecord
{
    public long Id { get; set; }

    public string SourceText { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public string DegreeText { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;

    public string Remark { get; set; } = string.Empty;

    public bool IsTrial { get; set; }
}
