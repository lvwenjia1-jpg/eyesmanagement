using OrderTextTrainer.Core.Models;
using WpfApp11;
using Xunit;

namespace pc.Tests;

public sealed class OrderDraftFactoryTests
{
    [Fact]
    public void CreateDrafts_ShouldKeepExplicitWearPeriod_ForHalfYearSegment()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("半年抛", "次元梦境Pro紫", "0", "HY-001"),
            CreateCatalogEntry("半年抛", "星辰泪青", "0", "HY-002"),
            CreateCatalogEntry("半年抛", "星辰泪蓝", "0", "HY-003"),
            CreateCatalogEntry("半年抛", "流萤森金棕", "0", "HY-004")
        });

        var text = "Lenspop半年抛，次元梦境pro紫/0一副 星辰泪青0/0一副 星辰泪蓝0/0一副 流萤森金棕/0一副 共4副";

        var drafts = factory.CreateDrafts(text, snapshot, null, out _);

        var draft = Assert.Single(drafts);
        Assert.Equal(4, draft.Items.Count);
        Assert.All(draft.Items, item => Assert.Equal("半年抛", item.WearPeriod));
        Assert.Contains(draft.Items, item => item.ProductName.Contains("次元梦境Pro紫", StringComparison.OrdinalIgnoreCase) && item.DegreeText == "0");
    }

    [Fact]
    public void CreateDrafts_ShouldInferUniqueWearPeriodFromCatalog_WhenTextHasNoWearPeriod()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("年抛", "绘世纱蓝", "100", "YEAR-001"),
            CreateCatalogEntry("年抛", "绘世纱蓝", "375", "YEAR-002")
        });

        var text = """
                   lenspop
                   绘世纱蓝 100/100
                   绘世纱蓝 375/375
                   小咩 13287852759
                   山东省威海市环翠区贝卡尔特路91号
                   """;

        var drafts = factory.CreateDrafts(text, snapshot, null, out _);

        var draft = Assert.Single(drafts);
        Assert.Equal(2, draft.Items.Count);
        Assert.All(draft.Items, item => Assert.Equal("年抛", item.WearPeriod));
    }

    [Fact]
    public void CreateDrafts_ShouldDefaultLenspopWithoutExplicitPeriod_ToHalfYear_WhenCatalogHasMultiplePeriodsForSameModel()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("年抛", "绘世纱蓝", "100", "YEAR-001"),
            CreateCatalogEntry("半年抛", "绘世纱蓝", "100", "HALF-001")
        });

        var text = """
                   lenspop
                   绘世纱蓝 100/100
                   小咩 13287852759
                   山东省威海市环翠区贝卡尔特路91号
                   """;

        var drafts = factory.CreateDrafts(text, snapshot, null, out _);

        var draft = Assert.Single(drafts);
        var item = Assert.Single(draft.Items);
        Assert.Equal("半年抛", item.WearPeriod);
    }

    [Fact]
    public void CreateDrafts_ShouldMapTrialAliasToConfiguredWearPeriod()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "流心珠蓝绿", "550", "DAY-001")
        });
        snapshot.WearPeriods = new List<LookupValueRow>
        {
            new() { Value = "日抛2片" }
        };
        snapshot.WearPeriodMappings = new List<WearPeriodMappingRow>
        {
            new() { Alias = "试戴片", WearPeriod = "日抛2片" }
        };

        var text = """
                   lenspop日抛两片装
                   试戴片
                   流心珠蓝绿550
                   """;

        var drafts = factory.CreateDrafts(text, snapshot, null, out _);

        var draft = Assert.Single(drafts);
        var item = Assert.Single(draft.Items);
        Assert.Equal("日抛2片", item.WearPeriod);
    }

    [Fact]
    public void CreateDraftsInBatches_ShouldInvokeCallbackPerBatch()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("半年抛", "绘世纱蓝", "100", "HALF-001")
        });

        var text = """
                   甲 13800000001 广东省深圳市南山区科技园
                   绘世纱蓝 100/100
                   乙 13800000002 广东省深圳市福田区深南大道
                   绘世纱蓝 100/100
                   丙 13800000003 广东省深圳市罗湖区人民南路
                   绘世纱蓝 100/100
                   """;

        var batches = new List<IReadOnlyList<OrderDraft>>();
        var drafts = factory.CreateDraftsInBatches(text, snapshot, null, 1, batch => batches.Add(batch), out _);

        Assert.Equal(3, drafts.Count);
        Assert.Equal(3, batches.Count);
        Assert.All(batches, batch => Assert.Single(batch));
        Assert.All(batches.SelectMany(batch => batch), draft => Assert.False(string.IsNullOrWhiteSpace(draft.OrderNumber)));
    }

    private static WorkflowSettingsSnapshot BuildSnapshot(IEnumerable<ProductCatalogEntry> catalog)
    {
        return new WorkflowSettingsSnapshot
        {
            RuleSet = ParserRuleSet.CreateDefault(),
            WearPeriods = new List<LookupValueRow>
            {
                new() { Value = "日抛2片" },
                new() { Value = "日抛10片" },
                new() { Value = "半年抛" },
                new() { Value = "年抛" },
                new() { Value = "试戴片" }
            },
            WearPeriodMappings = new List<WearPeriodMappingRow>
            {
                new() { Alias = "日抛", WearPeriod = "日抛2片" },
                new() { Alias = "半年抛", WearPeriod = "半年抛" },
                new() { Alias = "年抛", WearPeriod = "年抛" },
                new() { Alias = "试戴", WearPeriod = "试戴片" }
            },
            ProductCatalog = catalog.ToList()
        };
    }

    private static ProductCatalogEntry CreateCatalogEntry(string wearPeriod, string modelName, string degree, string productCode)
    {
        return new ProductCatalogEntry
        {
            ProductCode = productCode,
            ProductName = productCode,
            BaseName = $"{wearPeriod}{modelName}",
            SpecificationToken = wearPeriod,
            ModelToken = modelName,
            Degree = degree,
            SearchText = $"{wearPeriod} {modelName} {degree}"
        };
    }
}
