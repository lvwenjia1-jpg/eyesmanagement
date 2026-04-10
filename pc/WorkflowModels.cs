using System.Collections.ObjectModel;

namespace WpfApp11;

public sealed class LookupValueRow
{
    public string Value { get; set; } = string.Empty;
}

public sealed class WearPeriodMappingRow
{
    public string Alias { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;
}

public sealed class ProductCodeMappingRow
{
    public string Alias { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;
}

public sealed class ProductCodeOption
{
    public string ProductCode { get; set; } = string.Empty;

    public string CoreCode { get; set; } = string.Empty;

    public string DisplayText { get; set; } = string.Empty;

    public string SearchText { get; set; } = string.Empty;

    public string Initials { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public int MatchScore { get; set; }

    public int MatchFieldCount { get; set; }
}

public sealed class UserAccountRow
{
    public string LoginName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ErpId { get; set; } = string.Empty;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(DisplayName)
            ? LoginName
            : $"{DisplayName} ({LoginName})";
    }
}

public sealed class UploadConfiguration
{
    public string ApiUrl { get; set; } = "https://erp-open.hupun.com/api";

    public string AppKey { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public string OperatorErpFieldName { get; set; } = string.Empty;

    public string GiftFieldName { get; set; } = string.Empty;

    public string ItemWearPeriodFieldName { get; set; } = string.Empty;
}

public sealed class MainApiConfiguration
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:5249";

    public string LoginName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string MachineCode { get; set; } = Environment.MachineName;

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(LoginName) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(MachineCode);
}

public sealed class WorkflowSettingsSnapshot
{
    public OrderTextTrainer.Core.Models.ParserRuleSet RuleSet { get; set; } = OrderTextTrainer.Core.Models.ParserRuleSet.CreateDefault();

    public List<LookupValueRow> WearPeriods { get; set; } = new();

    public List<WearPeriodMappingRow> WearPeriodMappings { get; set; } = new();

    public List<OrderTextTrainer.Core.Models.ProductCatalogEntry> ProductCatalog { get; set; } = new();

    public List<ProductCodeMappingRow> ProductCodeMappings { get; set; } = new();

    public List<UserAccountRow> UserAccounts { get; set; } = new();

    public UploadConfiguration Upload { get; set; } = new();

    public MainApiConfiguration MainApi { get; set; } = new();
}

public sealed class OrderDraft
{
    public string DraftId { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public int OrderIndex { get; set; }

    public string RawText { get; set; } = string.Empty;

    public string ReceiverName { get; set; } = string.Empty;

    public string ReceiverMobile { get; set; } = string.Empty;

    public string ReceiverAddress { get; set; } = string.Empty;

    public string Remark { get; set; } = string.Empty;

    public bool HasGift { get; set; }

    public string OperatorLoginName { get; set; } = string.Empty;

    public string OperatorErpId { get; set; } = string.Empty;

    public string Status { get; set; } = "待审核";

    public string StatusDetail { get; set; } = string.Empty;

    public string ParseWarnings { get; set; } = string.Empty;

    public ObservableCollection<OrderItemDraft> Items { get; set; } = new();

    public string GoodsSummary => string.Join(" / ",
        Items.Select(item => $"{item.ProductCodeOrPlaceholder} x{item.QuantityTextOrPlaceholder}"));
}

public sealed class OrderItemDraft
{
    public string SourceText { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string BarcodeText { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;

    public string QuantityText { get; set; } = "1";

    public string Remark { get; set; } = string.Empty;

    public string DegreeText { get; set; } = string.Empty;

    public List<string> DegreeOptions { get; set; } = new();

    public bool IsTrial { get; set; }

    public string MatchHint { get; set; } = string.Empty;

    public List<ProductCodeOption> ProductCodeOptions { get; set; } = new();

    public string ProductCodeOrPlaceholder => string.IsNullOrWhiteSpace(ProductCode) ? "未选编码" : ProductCode;

    public string QuantityTextOrPlaceholder => string.IsNullOrWhiteSpace(QuantityText) ? "0" : QuantityText;
}

public sealed class OrderHistoryEntry
{
    public string DraftId { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string ReceiverName { get; set; } = string.Empty;

    public string ReceiverMobile { get; set; } = string.Empty;

    public string ReceiverAddress { get; set; } = string.Empty;

    public string GoodsSummary { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string StatusDetail { get; set; } = string.Empty;

    public string OperatorLoginName { get; set; } = string.Empty;

    public string RawText { get; set; } = string.Empty;

    public string ResponseText { get; set; } = string.Empty;
}

public sealed class OrderAuditRecord
{
    public string RecordId { get; set; } = string.Empty;

    public string DraftId { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string ActionType { get; set; } = string.Empty;

    public string ReceiverName { get; set; } = string.Empty;

    public string ReceiverMobile { get; set; } = string.Empty;

    public string ReceiverAddress { get; set; } = string.Empty;

    public string GoodsSummary { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string OperatorLoginName { get; set; } = string.Empty;

    public string RawText { get; set; } = string.Empty;

    public string SnapshotJson { get; set; } = string.Empty;

    public string ResponseText { get; set; } = string.Empty;
}

public sealed class OrderValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public List<string> Errors { get; } = new();

    public override string ToString()
    {
        return Errors.Count == 0 ? "校验通过。" : string.Join(Environment.NewLine, Errors);
    }
}
