using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class ListUploadsRequest : PagedQueryRequest
{
    public DateTime? Date { get; set; }

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    public string MachineCode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string UploaderLoginName { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public string ReceiverKeyword { get; set; } = string.Empty;

    public string DraftId { get; set; } = string.Empty;
}

public sealed class CreateUploadRequest
{
    [Required]
    public string DraftId { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string UploaderLoginName { get; set; } = string.Empty;

    public long? BusinessGroupId { get; set; }

    public string BusinessGroupName { get; set; } = string.Empty;

    [Required]
    public string ReceiverName { get; set; } = string.Empty;

    [Required]
    public string ReceiverMobile { get; set; } = string.Empty;

    [Required]
    public string ReceiverAddress { get; set; } = string.Empty;

    public string Remark { get; set; } = string.Empty;

    public bool HasGift { get; set; }

    public string Status { get; set; } = "已接收";

    public string StatusDetail { get; set; } = string.Empty;

    public string ExternalRequestJson { get; set; } = string.Empty;

    public string ExternalResponseJson { get; set; } = string.Empty;

    [MinLength(1)]
    public List<CreateUploadItemRequest> Items { get; set; } = new();
}

public sealed class CreateUploadItemRequest
{
    public string SourceText { get; set; } = string.Empty;

    [Required]
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;

    public string DegreeText { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;

    public string Remark { get; set; } = string.Empty;

    public bool IsTrial { get; set; }
}
