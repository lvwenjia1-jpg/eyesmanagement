using System.Collections.Concurrent;
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
    private const string DefaultUploadAppKey = "T3864192136";
    private const string DefaultUploadSecret = "f797cf33b33fde95879010922138a0f4";
    private const string LegacyUploadApiBaseUrl = "https://open-api.hupun.com/api";
    private const string LegacyUploadApiBaseUrl2 = "https://erp-open.hupun.com/api";
    private const string LegacyUploadApiUrl = "https://erp-open.hupun.com/api/erp/b2c/trades/open";
    private const string PreferredUploadApiUrl = "https://open-api.hupun.com/api/erp/b2c/trades/open";
    private const string LegacyMainApiUrlHttp127 = "http://127.0.0.1:5249";
    private const string LegacyMainApiUrlHttpLocalhost = "http://localhost:5249";
    private const string LegacyMainApiUrlHttps7018 = "https://localhost:7018";
    private const string PreferredMainApiUrl = "http://47.107.154.255:98";

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
        return RuntimeDataPathHelper.GetDataFilePath("workflow-settings.json");
    }

    public WorkflowSettingsSnapshot LoadOrCreate()
    {
        var path = GetDefaultSnapshotPath();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var snapshot = SanitizeSnapshot(JsonSerializer.Deserialize<WorkflowSettingsSnapshot>(json, JsonOptions) ?? BuildBootstrapSnapshot());
                return NormalizeMainApiConfiguration(NormalizeLegacyUploadConfiguration(HydratePreferredCatalog(snapshot)));
            }
            catch
            {
                BackupCorruptedSnapshot(path);
            }
        }

        var bootstrapSnapshot = BuildBootstrapSnapshot();
        Save(bootstrapSnapshot);
        return NormalizeMainApiConfiguration(NormalizeLegacyUploadConfiguration(bootstrapSnapshot));
    }

    public void Save(WorkflowSettingsSnapshot snapshot)
    {
        snapshot = SanitizeSnapshot(snapshot);
        if (snapshot.ProductCatalog.Count == 0)
        {
            var localCatalog = _catalogRepository.LoadCatalogIfExists();
            if (localCatalog.Count > 0)
            {
                snapshot.ProductCatalog = localCatalog.ToList();
            }
        }

        NormalizeMainApiConfiguration(NormalizeLegacyUploadConfiguration(snapshot));
        Directory.CreateDirectory(Path.GetDirectoryName(GetDefaultSnapshotPath())!);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        BackupExistingFile(GetDefaultSnapshotPath());
        File.WriteAllText(GetDefaultSnapshotPath(), json);

        snapshot.RuleSet.WearTypeAliases = BuildWearAliasDictionary(snapshot);
        _ruleRepository.Save(snapshot.RuleSet);
        _catalogRepository.SaveCatalog(snapshot.ProductCatalog);
    }

    public void Export(WorkflowSettingsSnapshot snapshot, string path)
    {
        snapshot = SanitizeSnapshot(snapshot);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(path, json);
    }

    public WorkflowSettingsSnapshot Import(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var snapshot = SanitizeSnapshot(JsonSerializer.Deserialize<WorkflowSettingsSnapshot>(json, JsonOptions) ?? BuildBootstrapSnapshot());
            return NormalizeMainApiConfiguration(NormalizeLegacyUploadConfiguration(snapshot));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加载配置文件失败，文件可能已损坏或包含乱码：{path}", ex);
        }
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

        return SanitizeSnapshot(new WorkflowSettingsSnapshot
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
                ApiUrl = PreferredUploadApiUrl,
                AppKey = DefaultUploadAppKey,
                Secret = DefaultUploadSecret,
                OperatorErpFieldName = string.Empty,
                GiftFieldName = string.Empty,
                ItemWearPeriodFieldName = string.Empty
            }
        });
    }

    private static WorkflowSettingsSnapshot SanitizeSnapshot(WorkflowSettingsSnapshot snapshot)
    {
        snapshot.RuleSet ??= ParserRuleSet.CreateDefault();
        snapshot.RuleSet.WearTypeAliases ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (snapshot.RuleSet.WearTypeAliases.TryGetValue("半年抛", out var aliases) && aliases is not null)
        {
            snapshot.RuleSet.WearTypeAliases["半年抛"] = aliases
                .Where(alias => !string.Equals(Safe(alias), "LENSPOP", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        snapshot.WearPeriodMappings ??= new List<WearPeriodMappingRow>();
        snapshot.WearPeriodMappings = snapshot.WearPeriodMappings
            .Where(row =>
                !(string.Equals(Safe(row.WearPeriod), "半年抛", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(Safe(row.Alias), "LENSPOP", StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(row => $"{Safe(row.WearPeriod)}|{Safe(row.Alias)}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        return snapshot;
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

    private static WorkflowSettingsSnapshot NormalizeLegacyUploadConfiguration(WorkflowSettingsSnapshot snapshot)
    {
        if (snapshot.Upload is null)
        {
            snapshot.Upload = new UploadConfiguration();
            return snapshot;
        }

        if (string.IsNullOrWhiteSpace(snapshot.Upload.ApiUrl) ||
            string.Equals(snapshot.Upload.ApiUrl.Trim(), LegacyUploadApiBaseUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snapshot.Upload.ApiUrl.Trim(), LegacyUploadApiBaseUrl2, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snapshot.Upload.ApiUrl.Trim(), LegacyUploadApiUrl, StringComparison.OrdinalIgnoreCase))
        {
            snapshot.Upload.ApiUrl = PreferredUploadApiUrl;
        }

        if (string.IsNullOrWhiteSpace(snapshot.Upload.AppKey))
        {
            snapshot.Upload.AppKey = DefaultUploadAppKey;
        }

        if (string.IsNullOrWhiteSpace(snapshot.Upload.Secret))
        {
            snapshot.Upload.Secret = DefaultUploadSecret;
        }

        return snapshot;
    }

    private static WorkflowSettingsSnapshot NormalizeMainApiConfiguration(WorkflowSettingsSnapshot snapshot)
    {
        snapshot.MainApi ??= new MainApiConfiguration();
        var normalizedBaseUrl = NormalizeMainApiBaseUrl(snapshot.MainApi.BaseUrl);
        if (string.IsNullOrWhiteSpace(normalizedBaseUrl) ||
            string.Equals(normalizedBaseUrl, LegacyMainApiUrlHttp127, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedBaseUrl, LegacyMainApiUrlHttpLocalhost, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedBaseUrl, LegacyMainApiUrlHttps7018, StringComparison.OrdinalIgnoreCase))
        {
            snapshot.MainApi.BaseUrl = PreferredMainApiUrl;
            return snapshot;
        }

        snapshot.MainApi.BaseUrl = normalizedBaseUrl;
        return snapshot;
    }

    private static string NormalizeMainApiBaseUrl(string? baseUrl)
    {
        var value = baseUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value.TrimEnd('/');
        }

        var path = uri.AbsolutePath;
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase))
        {
            path = "/";
        }

        var builder = new UriBuilder(uri)
        {
            Path = path,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string Safe(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static void BackupCorruptedSnapshot(string path)
    {
        try
        {
            var backupPath = $"{path}.broken-{DateTime.Now:yyyyMMddHHmmss}.json";
            File.Copy(path, backupPath, overwrite: false);
        }
        catch
        {
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static void BackupExistingFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length == 0)
        {
            return;
        }

        var backupPath = $"{path}.bak-{DateTime.Now:yyyyMMddHHmmss}";
        File.Copy(path, backupPath, overwrite: false);

        var directory = fileInfo.Directory;
        if (directory is null)
        {
            return;
        }

        foreach (var staleBackup in directory
                     .GetFiles($"{fileInfo.Name}.bak-*")
                     .OrderByDescending(item => item.LastWriteTimeUtc)
                     .Skip(10))
        {
            try
            {
                staleBackup.Delete();
            }
            catch
            {
            }
        }
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
        return RuntimeDataPathHelper.GetDataFilePath("order-history.json");
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
        var path = GetDefaultHistoryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
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
        return RuntimeDataPathHelper.GetDataFilePath("order-audit-log.json");
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
        var path = GetDefaultAuditPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    public void Append(OrderAuditRecord record)
    {
        var records = LoadOrCreate();
        records.Insert(0, record);
        Save(records);
    }
}

public sealed class UploadLearningSampleRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentQueue<string> _pendingLines = new();
    private readonly SemaphoreSlim _flushSignal = new(0);
    private readonly CancellationTokenSource _flushCancellation = new();
    private readonly Task _flushTask;

    public UploadLearningSampleRepository()
    {
        _flushTask = Task.Run(FlushLoopAsync);
    }

    public string GetDefaultPath()
    {
        return RuntimeDataPathHelper.GetDataFilePath("upload-learning-samples.jsonl");
    }

    public void Append(UploadLearningSampleRecord record)
    {
        var json = JsonSerializer.Serialize(record, JsonOptions);
        _pendingLines.Enqueue(json);
        _flushSignal.Release();
    }

    public void Dispose()
    {
        _flushCancellation.Cancel();
        _flushSignal.Release();

        try
        {
            _flushTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        FlushPendingLines();
        _flushSignal.Dispose();
        _flushCancellation.Dispose();
    }

    private async Task FlushLoopAsync()
    {
        while (!_flushCancellation.IsCancellationRequested)
        {
            try
            {
                await _flushSignal.WaitAsync(_flushCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await Task.Delay(120, _flushCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }

            await FlushPendingLinesAsync();
        }
    }

    private async Task FlushPendingLinesAsync()
    {
        var content = BuildPendingContent();
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        try
        {
            var path = GetDefaultPath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.AppendAllTextAsync(path, content, _flushCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            FlushContentBack(content);
        }
        catch
        {
            FlushContentBack(content);
        }
    }

    private void FlushPendingLines()
    {
        var content = BuildPendingContent();
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        try
        {
            var path = GetDefaultPath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(path, content);
        }
        catch
        {
        }
    }

    private string BuildPendingContent()
    {
        if (_pendingLines.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        while (_pendingLines.TryDequeue(out var line))
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private void FlushContentBack(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        foreach (var line in content.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
        {
            _pendingLines.Enqueue(line);
        }
    }
}

public sealed class OrderDraftFactory
{
    private readonly OrderTextParser _parser = new();

    public IReadOnlyList<OrderDraft> CreateDrafts(string rawText, WorkflowSettingsSnapshot snapshot, UserAccountRow? operatorAccount, out ParseResult parseResult)
    {
        return CreateDraftsInBatches(rawText, snapshot, operatorAccount, batchSize: int.MaxValue, onBatchReady: null, out parseResult);
    }

    public IReadOnlyList<OrderDraft> CreateDraftsInBatches(
        string rawText,
        WorkflowSettingsSnapshot snapshot,
        UserAccountRow? operatorAccount,
        int batchSize,
        Action<IReadOnlyList<OrderDraft>>? onBatchReady,
        out ParseResult parseResult)
    {
        // Build the runtime aliases once per parse request so every order in the batch
        // shares the same normalized matching view of brands, wear periods and products.
        var runtimeRuleSet = BuildRuntimeRuleSet(snapshot);
        parseResult = new ParseResult();
        var sessionId = BuildSessionId(rawText);
        var drafts = new List<OrderDraft>();
        var batch = new List<OrderDraft>();
        var batchThreshold = Math.Max(1, batchSize);
        var orderNumberSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var order in _parser.ParseOrders(rawText, runtimeRuleSet, snapshot.ProductCatalog, parseResult))
        {
            var draft = BuildDraft(
                order,
                snapshot,
                operatorAccount,
                sessionId,
                drafts.Count + 1,
                parseResult.Warnings,
                orderNumberSeed++);
            drafts.Add(draft);
            batch.Add(draft);

            if (batch.Count < batchThreshold)
            {
                continue;
            }

            onBatchReady?.Invoke(batch.ToList());
            batch.Clear();
        }

        if (batch.Count > 0)
        {
            onBatchReady?.Invoke(batch.ToList());
        }

        return drafts;
    }

    private static OrderDraft BuildDraft(
        ParsedOrder order,
        WorkflowSettingsSnapshot snapshot,
        UserAccountRow? operatorAccount,
        string sessionId,
        int draftIndex,
        IReadOnlyCollection<string> warnings,
        long orderNumberTimestamp)
    {
        var draft = new OrderDraft
        {
            DraftId = $"{sessionId}-{draftIndex:D3}",
            SessionId = sessionId,
            OrderIndex = draftIndex,
            RawText = order.SourceText,
            ReceiverName = Safe(order.CustomerName),
            ReceiverMobile = NormalizePhone(order.Phone),
            ReceiverAddress = Safe(order.Address),
            Remark = order.Remark ?? string.Empty,
            HasGift = order.Gifts.Count > 0,
            OperatorLoginName = operatorAccount?.LoginName ?? string.Empty,
            OperatorErpId = operatorAccount?.ErpId ?? string.Empty,
            Status = "待审核",
            ParseWarnings = string.Join("；", warnings)
        };

        foreach (var item in order.Items)
        {
            var itemWearPeriod = ResolveDraftItemWearPeriod(snapshot, order, item);
            draft.Items.Add(new OrderItemDraft
            {
                SourceText = item.RawText,
                ProductName = item.ProductName ?? string.Empty,
                WearPeriod = itemWearPeriod,
                QuantityText = Math.Max(item.Quantity ?? 1, 1).ToString(),
                Remark = item.Remark ?? string.Empty,
                DegreeText = ResolveDraftItemDegreeText(item),
                IsTrial = item.IsTrial || string.Equals(itemWearPeriod, "试戴片", StringComparison.OrdinalIgnoreCase),
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

        draft.OrderNumber = BuildOrderNumber(operatorAccount?.LoginName, orderNumberTimestamp);

        return draft;
    }

    private static string NormalizeOrderAccount(string? loginName)
    {
        var compact = Regex.Replace(Safe(loginName).ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
        return string.IsNullOrWhiteSpace(compact) ? "user" : compact;
    }

    private static string BuildOrderNumber(string? loginName, long timestamp)
    {
        return $"{NormalizeOrderAccount(loginName)}{timestamp}";
    }

    private static string ResolveDraftItemDegreeText(OrderItem item)
    {
        var explicitFromRaw = MatchTextHelper.ExtractExplicitDegreeKey(item.RawText);
        var normalizedFromPowerSummary = MatchTextHelper.NormalizeDegreeKey(item.PowerSummary);

        if (!string.IsNullOrWhiteSpace(explicitFromRaw) && !string.IsNullOrWhiteSpace(normalizedFromPowerSummary))
        {
            var rawHasMultiple = explicitFromRaw.Contains('/', StringComparison.OrdinalIgnoreCase);
            var summaryHasMultiple = normalizedFromPowerSummary.Contains('/', StringComparison.OrdinalIgnoreCase);

            if (rawHasMultiple && !summaryHasMultiple)
            {
                return normalizedFromPowerSummary;
            }

            if (!rawHasMultiple && summaryHasMultiple)
            {
                return explicitFromRaw;
            }

            if (!rawHasMultiple && !summaryHasMultiple &&
                !string.Equals(explicitFromRaw, normalizedFromPowerSummary, StringComparison.OrdinalIgnoreCase))
            {
                return explicitFromRaw;
            }

            return normalizedFromPowerSummary;
        }

        if (!string.IsNullOrWhiteSpace(explicitFromRaw))
        {
            return explicitFromRaw;
        }

        if (!string.IsNullOrWhiteSpace(normalizedFromPowerSummary))
        {
            return normalizedFromPowerSummary;
        }

        return MatchTextHelper.NormalizeDegreeKey(item.RawText);
    }

    private static string ResolveDraftItemWearPeriod(WorkflowSettingsSnapshot snapshot, ParsedOrder order, OrderItem item)
    {
        // Wear period may come from explicit text, catalog inference or alias mappings.
        // We resolve in that order so downstream SKU matching starts from the strongest clue.
        var explicitWearPeriod = DetectExplicitWearPeriod(snapshot, item);
        if (!string.IsNullOrWhiteSpace(explicitWearPeriod))
        {
            return explicitWearPeriod;
        }

        var inferredWearPeriod = InferWearPeriodFromCatalog(snapshot, item);
        if (!string.IsNullOrWhiteSpace(inferredWearPeriod))
        {
            return inferredWearPeriod;
        }

        var orderLevelWearHint = ResolveOrderLevelWearPeriodHint(snapshot, order);
        if (!string.IsNullOrWhiteSpace(orderLevelWearHint))
        {
            return orderLevelWearHint;
        }

        var sources = new[]
        {
            IsOrderLevelTrialOnly(order.WearPeriod) ? string.Empty : order.WearPeriod,
            IsOrderLevelTrialOnly(order.DetectedWearPeriod) ? string.Empty : order.DetectedWearPeriod,
            order.Brand,
            item.RawText,
            item.ProductName
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .ToList();

        var direct = snapshot.WearPeriods
            .Select(value => Safe(value.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderByDescending(value => MatchTextHelper.Compact(value).Length)
            .FirstOrDefault(value => sources.Any(source =>
            {
                var compactSource = MatchTextHelper.Compact(source);
                var compactValue = MatchTextHelper.Compact(value);
                return compactSource.Contains(compactValue, StringComparison.OrdinalIgnoreCase);
            }));
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var mapping = snapshot.WearPeriodMappings
            .Where(row => !string.IsNullOrWhiteSpace(row.Alias) && !string.IsNullOrWhiteSpace(row.WearPeriod))
            .OrderByDescending(row => MatchTextHelper.Compact(row.Alias).Length)
            .FirstOrDefault(row => sources.Any(source =>
            {
                var compactSource = MatchTextHelper.Compact(source);
                var compactAlias = MatchTextHelper.Compact(row.Alias);
                return compactSource.Contains(compactAlias, StringComparison.OrdinalIgnoreCase);
            }));

        if (!string.IsNullOrWhiteSpace(mapping?.WearPeriod))
        {
            return mapping.WearPeriod.Trim();
        }

        return ShouldDefaultLenspopToHalfYear(order, item)
            ? ResolveWearPeriodFromSettings(snapshot, "半年抛")
            : string.Empty;
    }

    /// <summary>
    /// Normalizes whole-order wear-period headers before we fall back to generic text matching.
    /// A brand/header like "lenspop日抛试戴片" should guide every item to daily wear instead of
    /// being treated as a standalone "试戴片" period and then accidentally defaulting to 半年抛.
    /// </summary>
    private static string ResolveOrderLevelWearPeriodHint(WorkflowSettingsSnapshot snapshot, ParsedOrder order)
    {
        var sources = new[]
            {
                order.Brand,
                order.WearPeriod,
                order.DetectedWearPeriod,
                order.SourceText
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToList();

        if (sources.Count == 0)
        {
            return string.Empty;
        }

        var hasTrialCue = sources.Any(source =>
            source.Contains("试戴", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("试用", StringComparison.OrdinalIgnoreCase));
        if (!hasTrialCue)
        {
            return string.Empty;
        }

        if (sources.Any(ContainsExplicitTenPieceDailyCue))
        {
            return ResolveWearPeriodFromSettings(snapshot, "日抛10片");
        }

        if (sources.Any(source => source.Contains("日抛", StringComparison.OrdinalIgnoreCase)))
        {
            return ResolveWearPeriodFromSettings(snapshot, "日抛2片");
        }

        return string.Empty;
    }

    private static string DetectExplicitWearPeriod(WorkflowSettingsSnapshot snapshot, OrderItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.LocalWearPeriodHint))
        {
            return ResolveWearPeriodFromSettings(snapshot, item.LocalWearPeriodHint);
        }

        var sources = new[]
            {
                item.RawText
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToList();

        foreach (var source in sources)
        {
            var explicitWearPeriod = MatchExplicitWearPeriod(source);
            if (!string.IsNullOrWhiteSpace(explicitWearPeriod))
            {
                return ResolveWearPeriodFromSettings(snapshot, explicitWearPeriod);
            }
        }

        return string.Empty;
    }

    private static bool IsOrderLevelTrialOnly(string? wearPeriod)
    {
        return string.Equals(Safe(wearPeriod), "试戴片", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsExplicitTenPieceDailyCue(string? source)
    {
        var text = Safe(source);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("日抛10片", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("日抛十片", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("日抛10片装", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("日抛十片装", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(text, @"(?:日抛|日拋)\s*(?:10片|十片|10片装|十片装)", RegexOptions.IgnoreCase);
    }

    private static string MatchExplicitWearPeriod(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        if (ContainsExplicitTenPieceDailyCue(source))
        {
            return "日抛10片";
        }

        if (source.Contains("日抛2片", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("日抛两片", StringComparison.OrdinalIgnoreCase))
        {
            return "日抛2片";
        }

        if (source.Contains("半年抛", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("半抛", StringComparison.OrdinalIgnoreCase))
        {
            return "半年抛";
        }

        if (source.Contains("年抛", StringComparison.OrdinalIgnoreCase))
        {
            return "年抛";
        }

        if (source.Contains("试戴", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("试用", StringComparison.OrdinalIgnoreCase))
        {
            return "试戴片";
        }

        if (source.Contains("日抛", StringComparison.OrdinalIgnoreCase))
        {
            return "日抛2片";
        }

        return string.Empty;
    }

    private static bool ShouldDefaultLenspopToHalfYear(ParsedOrder order, OrderItem item)
    {
        var sources = new[]
            {
                order.Brand,
                order.WearPeriod,
                order.DetectedWearPeriod,
                order.SourceText,
                item.RawText,
                item.ProductName
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToList();

        var containsLenspop = sources.Any(source => source.Contains("lenspop", StringComparison.OrdinalIgnoreCase));
        if (!containsLenspop)
        {
            return false;
        }

        var hasExplicitPeriod = sources.Any(source =>
            source.Contains("日抛", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("日拋", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("半年抛", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("半年拋", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("年抛", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("年拋", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("月抛", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("月拋", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("季抛", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("季拋", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("试戴", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("試戴", StringComparison.OrdinalIgnoreCase));

        return !hasExplicitPeriod;
    }

    private static string ResolveWearPeriodFromSettings(WorkflowSettingsSnapshot snapshot, string wearPeriod)
    {
        var cleanWearPeriod = Safe(wearPeriod);
        if (string.IsNullOrWhiteSpace(cleanWearPeriod))
        {
            return string.Empty;
        }

        var direct = snapshot.WearPeriods
            .Select(value => Safe(value.Value))
            .FirstOrDefault(value => string.Equals(value, cleanWearPeriod, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var compactWearPeriod = MatchTextHelper.Compact(cleanWearPeriod);
        var mapping = snapshot.WearPeriodMappings
            .Where(row => !string.IsNullOrWhiteSpace(row.Alias) && !string.IsNullOrWhiteSpace(row.WearPeriod))
            .OrderByDescending(row => MatchTextHelper.Compact(row.Alias).Length)
            .FirstOrDefault(row =>
            {
                var compactAlias = MatchTextHelper.Compact(row.Alias);
                return string.Equals(compactWearPeriod, compactAlias, StringComparison.OrdinalIgnoreCase) ||
                       compactWearPeriod.Contains(compactAlias, StringComparison.OrdinalIgnoreCase);
            });

        return string.IsNullOrWhiteSpace(mapping?.WearPeriod) ? cleanWearPeriod : mapping.WearPeriod.Trim();
    }

    private static string InferWearPeriodFromCatalog(WorkflowSettingsSnapshot snapshot, OrderItem item)
    {
        if (snapshot.ProductCatalog.Count == 0)
        {
            return string.Empty;
        }

        var candidateNames = new[]
            {
                item.ProductName,
                MatchTextHelper.RemoveTrailingDegree(item.ProductName),
                item.RawText
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidateNames.Count == 0)
        {
            return string.Empty;
        }

        var matchingEntries = snapshot.ProductCatalog
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ProductCode))
            .Where(entry => candidateNames.Any(name => CatalogEntryMatchesItemName(entry, name)))
            .ToList();

        if (matchingEntries.Count == 0)
        {
            return string.Empty;
        }

        var periods = matchingEntries
            .Select(entry => ResolveCatalogWearPeriod(snapshot, entry.SpecificationToken))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return periods.Count == 1 ? periods[0] : string.Empty;
    }

    private static bool CatalogEntryMatchesItemName(ProductCatalogEntry entry, string itemName)
    {
        var compactItemName = MatchTextHelper.Compact(itemName);
        if (string.IsNullOrWhiteSpace(compactItemName))
        {
            return false;
        }

        return GetCatalogAliases(entry)
            .Select(MatchTextHelper.Compact)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(alias =>
                compactItemName.Contains(alias, StringComparison.OrdinalIgnoreCase) ||
                alias.Contains(compactItemName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetCatalogAliases(ProductCatalogEntry entry)
    {
        foreach (var value in new[]
                 {
                     entry.ModelToken,
                     entry.BaseName,
                     entry.ProductName,
                     entry.ProductCode,
                     RemoveCatalogSpecificationPrefix(entry.ModelToken, entry.SpecificationToken),
                     RemoveCatalogSpecificationPrefix(entry.BaseName, entry.SpecificationToken),
                     RemoveCatalogSpecificationPrefix(entry.ProductName, entry.SpecificationToken),
                     RemoveCatalogSpecificationPrefix(entry.ProductCode, entry.SpecificationToken)
                 })
        {
            var clean = Safe(value);
            if (!string.IsNullOrWhiteSpace(clean))
            {
                yield return clean;
                yield return RemoveCatalogProMarker(clean);
            }
        }
    }

    private static string ResolveCatalogWearPeriod(WorkflowSettingsSnapshot snapshot, string? specificationToken)
    {
        var token = Safe(specificationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var compactToken = MatchTextHelper.Compact(token);
        var direct = snapshot.WearPeriods
            .Select(item => Safe(item.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderByDescending(value => MatchTextHelper.Compact(value).Length)
            .FirstOrDefault(value =>
            {
                var compactValue = MatchTextHelper.Compact(value);
                return string.Equals(compactToken, compactValue, StringComparison.OrdinalIgnoreCase) ||
                       compactToken.Contains(compactValue, StringComparison.OrdinalIgnoreCase);
            });

        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var mapping = snapshot.WearPeriodMappings
            .Where(row => !string.IsNullOrWhiteSpace(row.Alias) && !string.IsNullOrWhiteSpace(row.WearPeriod))
            .OrderByDescending(row => MatchTextHelper.Compact(row.Alias).Length)
            .FirstOrDefault(row =>
            {
                var compactAlias = MatchTextHelper.Compact(row.Alias);
                return string.Equals(compactToken, compactAlias, StringComparison.OrdinalIgnoreCase) ||
                       compactToken.Contains(compactAlias, StringComparison.OrdinalIgnoreCase);
            });

        return mapping?.WearPeriod?.Trim() ?? token;
    }

    private static string RemoveCatalogSpecificationPrefix(string? text, string? specificationToken)
    {
        var cleanText = Safe(text);
        var cleanSpecification = Safe(specificationToken);
        if (string.IsNullOrWhiteSpace(cleanText))
        {
            return string.Empty;
        }

        return !string.IsNullOrWhiteSpace(cleanSpecification) &&
               cleanText.StartsWith(cleanSpecification, StringComparison.OrdinalIgnoreCase)
            ? cleanText[cleanSpecification.Length..].Trim()
            : cleanText;
    }

    private static string RemoveCatalogProMarker(string? text)
    {
        var cleanText = Safe(text);
        return string.IsNullOrWhiteSpace(cleanText)
            ? string.Empty
            : Regex.Replace(cleanText, @"\s*pro\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
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

        phone = Regex.Replace(phone, @"\s+", string.Empty);
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
