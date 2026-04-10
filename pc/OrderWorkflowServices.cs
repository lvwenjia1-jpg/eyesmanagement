using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using OrderTextTrainer.Core.Models;
using OrderTextTrainer.Core.Services;

namespace WpfApp11;

public sealed class WorkflowSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    private readonly RuleRepository _ruleRepository = new();
    private readonly ProductCatalogRepository _catalogRepository = new();

    public string GetDefaultSnapshotPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "workflow-settings.json");
    }

    public WorkflowSettingsSnapshot LoadOrCreate()
    {
        var path = GetDefaultSnapshotPath();
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var snapshot = JsonSerializer.Deserialize<WorkflowSettingsSnapshot>(json, JsonOptions) ?? BuildBootstrapSnapshot();
            return HydratePreferredCatalog(snapshot);
        }

        var bootstrapSnapshot = BuildBootstrapSnapshot();
        Save(bootstrapSnapshot);
        return bootstrapSnapshot;
    }

    public void Save(WorkflowSettingsSnapshot snapshot)
    {
        Directory.CreateDirectory(AppContext.BaseDirectory);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(GetDefaultSnapshotPath(), json);

        snapshot.RuleSet.WearTypeAliases = BuildWearAliasDictionary(snapshot);
        _ruleRepository.Save(snapshot.RuleSet);
        _catalogRepository.SaveCatalog(snapshot.ProductCatalog);
    }

    public void Export(WorkflowSettingsSnapshot snapshot, string path)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(path, json);
    }

    public WorkflowSettingsSnapshot Import(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WorkflowSettingsSnapshot>(json, JsonOptions) ?? BuildBootstrapSnapshot();
    }

    public Dictionary<string, List<string>> BuildWearAliasDictionary(WorkflowSettingsSnapshot snapshot)
    {
        var map = snapshot.WearPeriods
            .Select(item => Safe(item.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(value => value, value => new List<string> { value }, StringComparer.OrdinalIgnoreCase);

        foreach (var row in snapshot.WearPeriodMappings)
        {
            var canonical = Safe(row.WearPeriod);
            var alias = Safe(row.Alias);
            if (string.IsNullOrWhiteSpace(canonical) || string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            if (!map.TryGetValue(canonical, out var aliases))
            {
                aliases = new List<string> { canonical };
                map[canonical] = aliases;
            }

            if (!aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                aliases.Add(alias);
            }
        }

        return map;
    }

    private WorkflowSettingsSnapshot BuildBootstrapSnapshot()
    {
        var ruleSet = _ruleRepository.LoadOrCreate();
        var catalog = _catalogRepository.LoadOrCreateCatalog().ToList();

        var wearPeriods = ruleSet.WearTypeAliases.Keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new LookupValueRow { Value = value })
            .ToList();

        var wearMappings = ruleSet.WearTypeAliases
            .SelectMany(group => group.Value.Select(alias => new WearPeriodMappingRow
            {
                Alias = alias,
                WearPeriod = group.Key
            }))
            .DistinctBy(item => $"{item.WearPeriod}|{item.Alias}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        var productMappings = new List<ProductCodeMappingRow>();
        foreach (var rule in ruleSet.ProductAliases)
        {
            var productCode = catalog.FirstOrDefault(entry =>
                string.Equals(entry.ProductName, rule.CanonicalName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.BaseName, rule.CanonicalName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.ModelToken, rule.CanonicalName, StringComparison.OrdinalIgnoreCase))?.ProductCode ?? string.Empty;

            foreach (var alias in rule.Aliases.Where(alias => !string.Equals(alias, rule.CanonicalName, StringComparison.OrdinalIgnoreCase)))
            {
                productMappings.Add(new ProductCodeMappingRow
                {
                    Alias = alias,
                    ProductCode = productCode,
                    Note = rule.CanonicalName
                });
            }
        }

        return new WorkflowSettingsSnapshot
        {
            RuleSet = ruleSet,
            WearPeriods = wearPeriods,
            WearPeriodMappings = wearMappings,
            ProductCatalog = catalog,
            ProductCodeMappings = productMappings,
            UserAccounts = new List<UserAccountRow>
            {
                new()
                {
                    LoginName = "zhangjianguo_erp",
                    DisplayName = "张建国",
                    ErpId = "ERP001"
                }
            },
            Upload = new UploadConfiguration
            {
                ApiUrl = "https://erp-open.hupun.com/api",
                AppKey = "T3864192136",
                Secret = "f797cf33b33fde95879010922138a0f4",
                OperatorErpFieldName = string.Empty,
                GiftFieldName = string.Empty,
                ItemWearPeriodFieldName = string.Empty
            }
        };
    }

    private WorkflowSettingsSnapshot HydratePreferredCatalog(WorkflowSettingsSnapshot snapshot)
    {
        var preferredCatalog = _catalogRepository.LoadOrCreateCatalog();
        if (preferredCatalog.Count == 0)
        {
            return snapshot;
        }

        snapshot.ProductCatalog = preferredCatalog.ToList();
        return snapshot;
    }

    private static string Safe(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}

public sealed class OrderHistoryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public string GetDefaultHistoryPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "order-history.json");
    }

    public List<OrderHistoryEntry> LoadOrCreate()
    {
        var path = GetDefaultHistoryPath();
        if (!File.Exists(path))
        {
            Save(Array.Empty<OrderHistoryEntry>());
            return new List<OrderHistoryEntry>();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<OrderHistoryEntry>>(json, JsonOptions) ?? new List<OrderHistoryEntry>();
    }

    public void Save(IEnumerable<OrderHistoryEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(GetDefaultHistoryPath(), json);
    }

    public void Upsert(OrderHistoryEntry entry)
    {
        var history = LoadOrCreate();
        var index = history.FindIndex(item => string.Equals(item.DraftId, entry.DraftId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            history[index] = entry;
        }
        else
        {
            history.Insert(0, entry);
        }

        Save(history);
    }
}

public sealed class OrderAuditRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public string GetDefaultAuditPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "order-audit-log.json");
    }

    public List<OrderAuditRecord> LoadOrCreate()
    {
        var path = GetDefaultAuditPath();
        if (!File.Exists(path))
        {
            Save(Array.Empty<OrderAuditRecord>());
            return new List<OrderAuditRecord>();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<OrderAuditRecord>>(json, JsonOptions) ?? new List<OrderAuditRecord>();
    }

    public void Save(IEnumerable<OrderAuditRecord> records)
    {
        var json = JsonSerializer.Serialize(records, JsonOptions);
        File.WriteAllText(GetDefaultAuditPath(), json);
    }

    public void Append(OrderAuditRecord record)
    {
        var records = LoadOrCreate();
        records.Insert(0, record);
        Save(records);
    }
}

public sealed class OrderDraftFactory
{
    private readonly OrderTextParser _parser = new();

    public IReadOnlyList<OrderDraft> CreateDrafts(string rawText, WorkflowSettingsSnapshot snapshot, UserAccountRow? operatorAccount, out ParseResult parseResult)
    {
        var runtimeRuleSet = BuildRuntimeRuleSet(snapshot);
        parseResult = _parser.Parse(rawText, runtimeRuleSet, snapshot.ProductCatalog);

        var sessionId = BuildSessionId(rawText);
        var drafts = new List<OrderDraft>();

        for (var orderIndex = 0; orderIndex < parseResult.Orders.Count; orderIndex++)
        {
            var order = parseResult.Orders[orderIndex];
            var draft = new OrderDraft
            {
                DraftId = $"{sessionId}-{orderIndex + 1:D3}",
                SessionId = sessionId,
                OrderIndex = orderIndex + 1,
                RawText = order.SourceText,
                ReceiverName = order.CustomerName ?? string.Empty,
                ReceiverMobile = NormalizePhone(order.Phone),
                ReceiverAddress = order.Address ?? string.Empty,
                Remark = order.Remark ?? string.Empty,
                HasGift = order.Gifts.Count > 0,
                OperatorLoginName = operatorAccount?.LoginName ?? string.Empty,
                OperatorErpId = operatorAccount?.ErpId ?? string.Empty,
                Status = "待审核",
                ParseWarnings = string.Join("；", parseResult.Warnings)
            };

            foreach (var item in order.Items)
            {
                draft.Items.Add(new OrderItemDraft
                {
                    SourceText = item.RawText,
                    ProductName = item.ProductName ?? string.Empty,
                    WearPeriod = order.WearPeriod ?? string.Empty,
                    QuantityText = Math.Max(item.Quantity ?? 1, 1).ToString(),
                    Remark = string.Empty,
                    DegreeText = MatchTextHelper.NormalizeDegreeKey(item.PowerSummary ?? item.RawText),
                    IsTrial = item.IsTrial || string.Equals(order.WearPeriod, "试戴片", StringComparison.OrdinalIgnoreCase),
                    MatchHint = "待匹配商品编码。"
                });
            }

            if (draft.Items.Count == 0)
            {
                draft.Items.Add(new OrderItemDraft
                {
                    SourceText = order.SourceText,
                    QuantityText = "1",
                    Remark = string.Empty,
                    MatchHint = "未解析出商品，请手工补录。"
                });
                draft.Status = "待补全";
            }

            drafts.Add(draft);
        }

        return drafts;
    }

    public ParserRuleSet BuildRuntimeRuleSet(WorkflowSettingsSnapshot snapshot)
    {
        var runtime = CloneRuleSet(snapshot.RuleSet);
        runtime.WearTypeAliases = snapshot.WearPeriods
            .Select(item => Safe(item.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(value => value, value => new List<string> { value }, StringComparer.OrdinalIgnoreCase);

        foreach (var row in snapshot.WearPeriodMappings)
        {
            var canonical = Safe(row.WearPeriod);
            var alias = Safe(row.Alias);
            if (string.IsNullOrWhiteSpace(canonical) || string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            if (!runtime.WearTypeAliases.TryGetValue(canonical, out var aliases))
            {
                aliases = new List<string> { canonical };
                runtime.WearTypeAliases[canonical] = aliases;
            }

            if (!aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                aliases.Add(alias);
            }
        }

        foreach (var mapping in snapshot.ProductCodeMappings)
        {
            var alias = Safe(mapping.Alias);
            var productCode = Safe(mapping.ProductCode);
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(productCode))
            {
                continue;
            }

            var catalogEntry = snapshot.ProductCatalog.FirstOrDefault(item => string.Equals(item.ProductCode, productCode, StringComparison.OrdinalIgnoreCase));
            if (catalogEntry is null)
            {
                continue;
            }

            var canonicalName = !string.IsNullOrWhiteSpace(catalogEntry.ProductName)
                ? catalogEntry.ProductName
                : catalogEntry.ProductCode;

            var rule = runtime.ProductAliases.FirstOrDefault(item => string.Equals(item.CanonicalName, canonicalName, StringComparison.OrdinalIgnoreCase));
            if (rule is null)
            {
                rule = new ProductAliasRule
                {
                    CanonicalName = canonicalName,
                    Aliases = new List<string> { canonicalName }
                };
                runtime.ProductAliases.Add(rule);
            }

            if (!rule.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                rule.Aliases.Add(alias);
            }
        }

        return runtime;
    }

    private static ParserRuleSet CloneRuleSet(ParserRuleSet source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<ParserRuleSet>(json) ?? ParserRuleSet.CreateDefault();
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var landlineMatch = Regex.Match(phone, @"0\d{2,3}-?\d{7,8}(?:转\d{1,6})?");
        if (landlineMatch.Success)
        {
            return landlineMatch.Value.Replace(" ", string.Empty);
        }

        var digits = Regex.Replace(phone, @"[^\d]", string.Empty);
        if (digits.StartsWith("86", StringComparison.Ordinal) && digits.Length > 11)
        {
            digits = digits[2..];
        }

        return digits.Length > 11 ? digits[^11..] : digits;
    }

    private static string BuildSessionId(string rawText)
    {
        var raw = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|{rawText}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..10];
    }

    private static string Safe(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}

public sealed class OrderDraftValidator
{
    public OrderValidationResult Validate(OrderDraft draft, WorkflowSettingsSnapshot snapshot)
    {
        var result = new OrderValidationResult();

        if (string.IsNullOrWhiteSpace(draft.ReceiverName))
        {
            result.Errors.Add("收件人不能为空。");
        }

        if (!Regex.IsMatch(draft.ReceiverMobile ?? string.Empty, @"^(1[3-9]\d{9}|0\d{2,3}-?\d{7,8}(?:转\d{1,6})?)$"))
        {
            result.Errors.Add("联系电话必须是有效手机号或座机号。");
        }

        if (string.IsNullOrWhiteSpace(draft.ReceiverAddress))
        {
            result.Errors.Add("详细地址不能为空。");
        }

        if (string.IsNullOrWhiteSpace(draft.OperatorLoginName))
        {
            result.Errors.Add("请选择登录账号。");
        }

        if (string.IsNullOrWhiteSpace(draft.OperatorErpId))
        {
            result.Errors.Add("当前登录账号未配置 ERPID。");
        }

        if (draft.Items.Count == 0)
        {
            result.Errors.Add("至少需要一条商品信息。");
        }

        for (var index = 0; index < draft.Items.Count; index++)
        {
            var item = draft.Items[index];
            var prefix = $"商品 {index + 1}";

            if (string.IsNullOrWhiteSpace(item.ProductCode))
            {
                result.Errors.Add($"{prefix} 的商品编码不能为空。");
            }

            if (!int.TryParse(item.QuantityText, out var quantity) || quantity <= 0)
            {
                result.Errors.Add($"{prefix} 的数量必须是正整数。");
            }

            if (!string.IsNullOrWhiteSpace(item.ProductCode) &&
                snapshot.ProductCatalog.Count > 0 &&
                !snapshot.ProductCatalog.Any(entry => string.Equals(entry.ProductCode, item.ProductCode, StringComparison.OrdinalIgnoreCase)))
            {
                result.Errors.Add($"{prefix} 的商品编码不在商品列表中。");
            }
        }

        return result;
    }
}
