using OrderTextTrainer.Core.Models;
using WpfApp11;
using Xunit;

namespace pc.Tests;

public sealed class WorkflowMatchingRuleTests
{
    [Fact]
    public void CreateDrafts_ShouldDefaultLenspopWithoutWearPeriod_ToHalfYear()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("halfyear", "dreambluepro", "600", "SKU-HY-001")
        });

        var drafts = factory.CreateDrafts(
            """
            lenspop ten boxes
            dreambluepro600
            xiaoyu 18815123904 zhejiang wenzhou yueqing no8 road
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);
        var item = Assert.Single(draft.Items);
        Assert.Equal("halfyear", item.WearPeriod);
    }

    [Fact]
    public void RefreshDrafts_ShouldAutoFillUniquePartialCandidate_ButKeepPendingConfirmation()
    {
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("halfyear", "fantasypurple", "0", "SKU-HY-002")
        });
        var item = new OrderItemDraft
        {
            ProductName = "fantasypurple",
            SourceText = "fantasypurple"
        };

        resolver.RefreshItem(item, snapshot);

        Assert.Equal("SKU-HY-002", item.ProductCode);
        Assert.Equal("Partial", item.ProductMatchState);
        Assert.False(item.ProductCodeConfirmed);
    }

    [Fact]
    public void RefreshItem_ShouldNotAutoFill_WhenOnlyWearPeriodAndDegreeMatch()
    {
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("halfyear", "oceanblue", "225", "SKU-OCEAN-225")
        });
        var item = new OrderItemDraft
        {
            ProductName = "cherryred",
            WearPeriod = "halfyear",
            DegreeText = "225",
            SourceText = "cherryred 225"
        };

        resolver.RefreshItem(item, snapshot);

        Assert.Equal(string.Empty, item.ProductCode);
        Assert.Equal("Unmatched", item.ProductMatchState);
        Assert.False(item.ProductCodeConfirmed);
        Assert.Contains(item.ProductCodeOptions, option => option.ProductCode == "SKU-OCEAN-225");
    }

    [Fact]
    public void RefreshItem_ShouldNotTreatNumericTokenAsModelMatch()
    {
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            new ProductCatalogEntry
            {
                ProductCode = "SKU-MQ-500",
                ProductName = "SKU-MQ-500",
                BaseName = "半年抛玛奇玛500",
                SpecificationToken = "半年抛",
                ModelToken = string.Empty,
                Degree = "500",
                SearchText = "半年抛 玛奇玛 500"
            }
        });

        var item = new OrderItemDraft
        {
            ProductName = "冰沙糖红",
            WearPeriod = "半年抛",
            DegreeText = "500",
            SourceText = "冰沙糖红（左眼600 右眼500）*1"
        };

        resolver.RefreshItem(item, snapshot);

        Assert.Equal(string.Empty, item.ProductCode);
        Assert.Equal("Unmatched", item.ProductMatchState);
        Assert.False(item.ProductCodeConfirmed);
    }

    [Fact]
    public void RefreshItem_ShouldPromoteToExact_WhenTwoConditionsMatchAndThirdIsUnique()
    {
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "灵隙金", "0", "SKU-LXJ-0"),
            CreateCatalogEntry("日抛2片", "灵隙金", "100", "SKU-LXJ-100")
        });

        var item = new OrderItemDraft
        {
            ProductName = "灵隙金",
            DegreeText = "0",
            SourceText = "灵隙金各来3副0度"
        };

        resolver.RefreshItem(item, snapshot);

        Assert.Equal("SKU-LXJ-0", item.ProductCode);
        Assert.Equal("Exact", item.ProductMatchState);
        Assert.True(item.ProductCodeConfirmed);
    }

    [Fact]
    public void CreateDrafts_AndRefreshDraft_ShouldExactMatchDailyItems_FromOrderLevelWearPeriod()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "冰砂糖红", "375", "SKU-RED-375"),
            CreateCatalogEntry("日抛2片", "高光月球绿", "375", "SKU-GREEN-375")
        });

        var drafts = factory.CreateDrafts(
            """
            下单
            订单号: 10549956
            商品信息:513区LENSPOP 日抛 瞳物语 星 品上市 十盒 限时促销
            客户备注:冰砂糖红375度2副 高光月球绿375度2副
            瑞瑞 15601864170
            浙江省 杭州市 余杭区 仓前街道滨江枫秀云庭7幢1单元302室
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);
        Assert.Equal(2, draft.Items.Count);

        var parsedIcedSugar = Assert.Single(
            draft.Items,
            item => item.SourceText.Contains("冰砂糖红", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("日抛2片", parsedIcedSugar.WearPeriod);
        Assert.True(
            string.Equals("375", parsedIcedSugar.DegreeText, StringComparison.OrdinalIgnoreCase),
            $"{parsedIcedSugar.SourceText} => {parsedIcedSugar.DegreeText}");

        var parsedMoonGreen = Assert.Single(
            draft.Items,
            item => item.SourceText.Contains("高光月球绿", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("日抛2片", parsedMoonGreen.WearPeriod);
        Assert.True(
            string.Equals("375", parsedMoonGreen.DegreeText, StringComparison.OrdinalIgnoreCase),
            $"{parsedMoonGreen.SourceText} => {parsedMoonGreen.DegreeText}");

        resolver.RefreshDraft(draft, snapshot);

        var draftSummary = string.Join(" | ", draft.Items.Select(item =>
            $"{item.ProductName}:{item.WearPeriod}:{item.DegreeText}:{item.ProductCode}:{item.ProductMatchState}:{item.ProductCodeConfirmed}:{item.MatchHint}"));

        var icedSugar = Assert.Single(
            draft.Items,
            item => item.ProductName.Contains("冰砂糖红", StringComparison.OrdinalIgnoreCase) ||
                    item.SourceText.Contains("冰砂糖红", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("日抛2片", icedSugar.WearPeriod);
        Assert.Equal("375", icedSugar.DegreeText);
        Assert.Equal("SKU-RED-375", icedSugar.ProductCode);
        Assert.True(string.Equals("Exact", icedSugar.ProductMatchState, StringComparison.OrdinalIgnoreCase), draftSummary);
        Assert.True(icedSugar.ProductCodeConfirmed);

        var moonGreen = Assert.Single(
            draft.Items,
            item => item.ProductName.Contains("高光月球绿", StringComparison.OrdinalIgnoreCase) ||
                    item.SourceText.Contains("高光月球绿", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("日抛2片", moonGreen.WearPeriod);
        Assert.Equal("375", moonGreen.DegreeText);
        Assert.Equal("SKU-GREEN-375", moonGreen.ProductCode);
        Assert.True(string.Equals("Exact", moonGreen.ProductMatchState, StringComparison.OrdinalIgnoreCase), draftSummary);
        Assert.True(moonGreen.ProductCodeConfirmed);
    }

    [Fact]
    public void CreateDrafts_ShouldParseMultiplyFormats_ForDegreeAndQuantity()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "流荧森玫红", "225", "SKU-RH-225"),
            CreateCatalogEntry("日抛2片", "沉沦诗蓝", "325", "SKU-CL-325"),
            CreateCatalogEntry("日抛2片", "次元梦境pro灰", "650", "SKU-PG-650"),
            CreateCatalogEntry("日抛2片", "次元梦境紫", "800", "SKU-PU-800")
        });

        var drafts = factory.CreateDrafts(
            """
            下单
            商品信息:lenspop日抛
            客户备注:
            流荧森玫红 225*2
            沉沦诗蓝325＊3
            次元梦境pro灰650x2
            次元梦境紫x3 800
            张三 13800138000 浙江省杭州市余杭区xx路88号
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);

        var roseRed = Assert.Single(draft.Items, item => item.SourceText.Contains("流荧森玫红", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("225", roseRed.DegreeText);
        Assert.Equal("2", roseRed.QuantityText);

        var sinkingBlue = Assert.Single(draft.Items, item => item.SourceText.Contains("沉沦诗蓝", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("325", sinkingBlue.DegreeText);
        Assert.Equal("3", sinkingBlue.QuantityText);

        var proGray = Assert.Single(draft.Items, item => item.SourceText.Contains("次元梦境pro灰", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("650", proGray.DegreeText);
        Assert.Equal("2", proGray.QuantityText);

        var dreamPurple = Assert.Single(draft.Items, item => item.SourceText.Contains("次元梦境紫", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("800", dreamPurple.DegreeText);
        Assert.Equal("3", dreamPurple.QuantityText);
    }

    [Fact]
    public void CreateDrafts_ShouldKeepDualDegrees_ForSingleModelSeparatedBySlash()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("半年抛", "奶冻冰球红", "250", "SKU-MILK-250"),
            CreateCatalogEntry("半年抛", "奶冻冰球红", "400", "SKU-MILK-400"),
            CreateCatalogEntry("半年抛", "光织蓝", "250", "SKU-GZB-250"),
            CreateCatalogEntry("半年抛", "溏心珠绿", "250", "SKU-TXZ-250"),
            CreateCatalogEntry("半年抛", "光织绿", "225", "SKU-GZG-225")
        });

        var drafts = factory.CreateDrafts(
            """
            缺货
            姓名:竹
            电话:15826656862
            地址:湖北省荆州市江陵县郝穴镇江陵大道奥林花园小区
            品牌:lenspop清仓
            款式:1奶冻冰球红一个250度/一个400度
                  2光织蓝250度
                  3溏心珠绿250度
                  4光织绿225度
            赠品:每副送伴侣盒，每单护理液
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);
        var milkItems = draft.Items
            .Where(item => item.SourceText.Contains("奶冻冰球红", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(2, milkItems.Count);
        var milkDegrees = milkItems
            .Select(item => item.DegreeText)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(new[] { "250", "400" }, milkDegrees);
    }

    private static WorkflowSettingsSnapshot BuildSnapshot(IEnumerable<ProductCatalogEntry> catalog)
    {
        return new WorkflowSettingsSnapshot
        {
            RuleSet = ParserRuleSet.CreateDefault(),
            WearPeriods = new List<LookupValueRow>
            {
                new() { Value = "daily2" },
                new() { Value = "daily10" },
                new() { Value = "halfyear" },
                new() { Value = "yearly" },
                new() { Value = "trial" },
                new() { Value = "日抛2片" },
                new() { Value = "日抛10片" },
                new() { Value = "半年抛" },
                new() { Value = "年抛" },
                new() { Value = "试戴片" }
            },
            WearPeriodMappings = new List<WearPeriodMappingRow>
            {
                new() { Alias = "daily", WearPeriod = "daily2" },
                new() { Alias = "daily2", WearPeriod = "daily2" },
                new() { Alias = "daily10", WearPeriod = "daily10" },
                new() { Alias = "halfyear", WearPeriod = "halfyear" },
                new() { Alias = "trial", WearPeriod = "trial" },
                new() { Alias = "日抛", WearPeriod = "日抛2片" },
                new() { Alias = "日抛2片", WearPeriod = "日抛2片" },
                new() { Alias = "日抛两片", WearPeriod = "日抛2片" },
                new() { Alias = "日抛10片", WearPeriod = "日抛10片" },
                new() { Alias = "日抛十片", WearPeriod = "日抛10片" },
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
