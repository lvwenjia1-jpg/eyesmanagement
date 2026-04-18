using System.Diagnostics;
using OrderTextTrainer.Core.Services;
using WpfApp11;

var repeatCount = args.Length > 0 && int.TryParse(args[0], out var parsedRepeat) ? parsedRepeat : 20;
var measureIterations = args.Length > 1 && int.TryParse(args[1], out var parsedIterations) ? parsedIterations : 5;
var benchmark = new ParseBenchmark(repeatCount, measureIterations);
benchmark.Run();

internal sealed class ParseBenchmark
{
    private readonly int _repeatCount;
    private readonly int _measureIterations;

    public ParseBenchmark(int repeatCount, int measureIterations)
    {
        _repeatCount = Math.Max(1, repeatCount);
        _measureIterations = Math.Max(1, measureIterations);
    }

    public void Run()
    {
        var root = FindRepoRoot();
        var workflowPath = Path.Combine(root, "pc", "bin", "Debug", "net6.0-windows", "workflow-settings.json");
        var snapshot = new WorkflowSettingsRepository().Import(workflowPath);
        var rawText = string.Join(Environment.NewLine + Environment.NewLine,
            Enumerable.Repeat(SampleData.DefaultText, _repeatCount));
        var draftFactory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var account = snapshot.UserAccounts.FirstOrDefault();

        Console.WriteLine($"workflow={workflowPath}");
        Console.WriteLine($"catalog_count={snapshot.ProductCatalog.Count}");
        Console.WriteLine($"repeat_count={_repeatCount}");
        Console.WriteLine($"measure_iterations={_measureIterations}");

        // Warmup
        RunOnce(draftFactory, resolver, snapshot, rawText, account);

        var createDraftMs = new List<double>();
        var refreshMs = new List<double>();
        var totalMs = new List<double>();
        var draftCount = 0;
        var itemCount = 0;

        for (var index = 0; index < _measureIterations; index++)
        {
            var result = RunOnce(draftFactory, resolver, snapshot, rawText, account);
            draftCount = result.DraftCount;
            itemCount = result.ItemCount;
            createDraftMs.Add(result.CreateDraftElapsed.TotalMilliseconds);
            refreshMs.Add(result.RefreshElapsed.TotalMilliseconds);
            totalMs.Add(result.TotalElapsed.TotalMilliseconds);
        }

        Console.WriteLine($"draft_count={draftCount}");
        Console.WriteLine($"item_count={itemCount}");
        Console.WriteLine($"create_drafts_ms_avg={createDraftMs.Average():F2}");
        Console.WriteLine($"refresh_resolutions_ms_avg={refreshMs.Average():F2}");
        Console.WriteLine($"total_ms_avg={totalMs.Average():F2}");
        Console.WriteLine($"create_drafts_ms_min={createDraftMs.Min():F2}");
        Console.WriteLine($"refresh_resolutions_ms_min={refreshMs.Min():F2}");
        Console.WriteLine($"total_ms_min={totalMs.Min():F2}");
    }

    private static BenchmarkRunResult RunOnce(
        OrderDraftFactory draftFactory,
        CatalogSkuResolver resolver,
        WorkflowSettingsSnapshot snapshot,
        string rawText,
        UserAccountRow? account)
    {
        var totalWatch = Stopwatch.StartNew();

        var createWatch = Stopwatch.StartNew();
        var drafts = draftFactory.CreateDrafts(rawText, snapshot, account, out _)
            .Select(CloneDraft)
            .ToList();
        createWatch.Stop();

        var refreshWatch = Stopwatch.StartNew();
        resolver.RefreshDrafts(drafts, snapshot);
        refreshWatch.Stop();

        totalWatch.Stop();
        return new BenchmarkRunResult(
            createWatch.Elapsed,
            refreshWatch.Elapsed,
            totalWatch.Elapsed,
            drafts.Count,
            drafts.Sum(draft => draft.Items.Count));
    }

    private static OrderDraft CloneDraft(OrderDraft source)
    {
        return new OrderDraft
        {
            DraftId = source.DraftId,
            OrderNumber = source.OrderNumber,
            SessionId = source.SessionId,
            OrderIndex = source.OrderIndex,
            RawText = source.RawText,
            ReceiverName = source.ReceiverName,
            ReceiverMobile = source.ReceiverMobile,
            ReceiverAddress = source.ReceiverAddress,
            Remark = source.Remark,
            HasGift = source.HasGift,
            OperatorLoginName = source.OperatorLoginName,
            OperatorErpId = source.OperatorErpId,
            BusinessGroupId = source.BusinessGroupId,
            BusinessGroupName = source.BusinessGroupName,
            Status = source.Status,
            StatusDetail = source.StatusDetail,
            ParseWarnings = source.ParseWarnings,
            Items = new System.Collections.ObjectModel.ObservableCollection<OrderItemDraft>(
                source.Items.Select(item => new OrderItemDraft
                {
                    SourceText = item.SourceText,
                    ProductCode = item.ProductCode,
                    ProductName = item.ProductName,
                    BarcodeText = item.BarcodeText,
                    WearPeriod = item.WearPeriod,
                    QuantityText = item.QuantityText,
                    Remark = item.Remark,
                    DegreeText = item.DegreeText,
                    ProductCodeSearchKeyword = item.ProductCodeSearchKeyword,
                    ProductCodeSearchSummary = item.ProductCodeSearchSummary,
                    DegreeOptions = item.DegreeOptions.ToList(),
                    IsTrial = item.IsTrial,
                    MatchHint = item.MatchHint,
                    ProductMatchState = item.ProductMatchState,
                    ProductCodeConfirmed = item.ProductCodeConfirmed,
                    ProductWorkflowStage = item.ProductWorkflowStage,
                    ProductWorkflowDetail = item.ProductWorkflowDetail,
                    ProductMatchStatusText = item.ProductMatchStatusText,
                    ProductCodeOptions = item.ProductCodeOptions.ToList()
                }))
        };
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfApp11.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root.");
    }

    private sealed record BenchmarkRunResult(
        TimeSpan CreateDraftElapsed,
        TimeSpan RefreshElapsed,
        TimeSpan TotalElapsed,
        int DraftCount,
        int ItemCount);
}
