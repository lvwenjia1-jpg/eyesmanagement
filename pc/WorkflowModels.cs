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

public sealed class ProductCatalogGroupRow
{
    public string WearPeriod { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public int DegreeCount { get; set; }

    public List<ProductCatalogDegreeRow> Degrees { get; set; } = new();

    public string DegreeSummary => DegreeCount <= 0 ? "无度数" : $"{DegreeCount} 个度数";
}

public sealed class ProductCatalogDegreeRow
{
    public string DegreeText { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;
}

public sealed class ProductCodeOption
{
    public string ProductCode { get; set; } = string.Empty;

    public string CoreCode { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public string DegreeText { get; set; } = string.Empty;

    public string DisplayText { get; set; } = string.Empty;

    public string SearchText { get; set; } = string.Empty;

    public string Initials { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public int MatchScore { get; set; }

    public int MatchFieldCount { get; set; }

    public string MatchState { get; set; } = "Unmatched";

    public string MatchStateText { get; set; } = string.Empty;
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

public sealed class BusinessGroupOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Name) ? $"群组 {Id}" : Name;
    }
}

public sealed class UploadConfiguration
{
    public string ApiUrl { get; set; } = "https://open-api.hupun.com/api/erp/b2c/trades/open";

    public string AppKey { get; set; } = "T3864192136";

    public string Secret { get; set; } = "f797cf33b33fde95879010922138a0f4";

    public string ShopNick { get; set; } = "瞳物语手工单";

    public string OperatorErpFieldName { get; set; } = string.Empty;

    public string GiftFieldName { get; set; } = string.Empty;

    public string ItemWearPeriodFieldName { get; set; } = string.Empty;
}

public sealed class MainApiConfiguration
{
    public string BaseUrl { get; set; } = "https://localhost:5001";

    public string LoginName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string MachineCode { get; set; } = string.Empty;

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(LoginName) &&
        !string.IsNullOrWhiteSpace(Password);
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

    public long? BusinessGroupId { get; set; }

    public string BusinessGroupName { get; set; } = string.Empty;

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

    public string SpecCodeText { get; set; } = string.Empty;

    public string BarcodeText { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;

    public string QuantityText { get; set; } = "1";

    public string Remark { get; set; } = string.Empty;

    public string DegreeText { get; set; } = string.Empty;

    public string ProductCodeSearchKeyword { get; set; } = string.Empty;

    public string ProductCodeSearchSummary { get; set; } = string.Empty;

    public List<string> DegreeOptions { get; set; } = new();

    public bool IsTrial { get; set; }

    public string MatchHint { get; set; } = string.Empty;

    public string ProductMatchState { get; set; } = "Unmatched";

    public bool ProductCodeConfirmed { get; set; }

    public string ProductWorkflowStage { get; set; } = "待识别";

    public string ProductWorkflowDetail { get; set; } = string.Empty;

    public string ProductMatchStatusText { get; set; } = "未确认";

    public List<ProductCodeOption> ProductCodeOptions { get; set; } = new();

    public string ProductCodeOrPlaceholder => string.IsNullOrWhiteSpace(ProductCode) ? "未选编码" : ProductCode;

    public string ProductCodeDisplayText => string.IsNullOrWhiteSpace(ProductCode)
        ? (string.IsNullOrWhiteSpace(ProductConditionSummary)
            ? (string.IsNullOrWhiteSpace(ProductCodeSearchKeyword) ? "未选编码" : ProductCodeSearchKeyword)
            : ProductConditionSummary)
        : ProductCode;

    public string ProductConditionSummary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(WearPeriod))
            {
                parts.Add(WearPeriod.Trim());
            }

            if (!string.IsNullOrWhiteSpace(ProductName))
            {
                parts.Add(ProductName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(DegreeText))
            {
                parts.Add($"{DegreeText.Trim()}度");
            }

            return string.Join(" / ", parts);
        }
    }

    public string QuantityTextOrPlaceholder => string.IsNullOrWhiteSpace(QuantityText) ? "0" : QuantityText;

    public string ProductWorkflowSummary => string.IsNullOrWhiteSpace(ProductWorkflowDetail)
        ? ProductWorkflowStage
        : $"{ProductWorkflowStage}：{ProductWorkflowDetail}";
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

    public string OperatorErpId { get; set; } = string.Empty;

    public long? BusinessGroupId { get; set; }

    public string BusinessGroupName { get; set; } = string.Empty;

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

    public string OperatorErpId { get; set; } = string.Empty;

    public long? BusinessGroupId { get; set; }

    public string BusinessGroupName { get; set; } = string.Empty;

    public string RawText { get; set; } = string.Empty;

    public string SnapshotJson { get; set; } = string.Empty;

    public string ResponseText { get; set; } = string.Empty;
}

public sealed class UploadLearningSampleRecord
{
    public string RecordId { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string DraftId { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string ReceiverName { get; set; } = string.Empty;

    public string ReceiverMobile { get; set; } = string.Empty;

    public string ReceiverAddress { get; set; } = string.Empty;

    public string RawText { get; set; } = string.Empty;

    public string RequestUrl { get; set; } = string.Empty;

    public bool IsSuccess { get; set; }

    public string ResponseText { get; set; } = string.Empty;

    public string TradeDetailsJson { get; set; } = string.Empty;

    public List<UploadLearningItemRecord> DraftItems { get; set; } = new();

    public List<UploadLearningItemRecord> UploadedItems { get; set; } = new();
}

public sealed class UploadLearningItemRecord
{
    public string SourceText { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string SpecCodeText { get; set; } = string.Empty;

    public string BarcodeText { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;

    public string DegreeText { get; set; } = string.Empty;

    public string QuantityText { get; set; } = string.Empty;

    public bool IsTrial { get; set; }
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
