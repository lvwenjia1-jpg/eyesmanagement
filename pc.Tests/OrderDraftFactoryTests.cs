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
            CreateCatalogEntry("鍗婂勾鎶?, "娆″厓姊﹀Pro绱?, "0", "HY-001"),
            CreateCatalogEntry("鍗婂勾鎶?, "鏄熻景娉潚", "0", "HY-002"),
            CreateCatalogEntry("鍗婂勾鎶?, "鏄熻景娉摑", "0", "HY-003"),
            CreateCatalogEntry("鍗婂勾鎶?, "娴佽悿妫噾妫?, "0", "HY-004")
        });

        var text = "Lenspop鍗婂勾鎶涳紝娆″厓姊﹀pro绱?0涓€鍓?鏄熻景娉潚0/0涓€鍓?鏄熻景娉摑0/0涓€鍓?娴佽悿妫噾妫?0涓€鍓?鍏?鍓?;

        var drafts = factory.CreateDrafts(text, snapshot, null, out _);

        var draft = Assert.Single(drafts);
        Assert.Equal(4, draft.Items.Count);
        Assert.All(draft.Items, item => Assert.Equal("鍗婂勾鎶?, item.WearPeriod));
        Assert.Contains(draft.Items, item => item.ProductName.Contains("娆″厓姊﹀Pro绱?, StringComparison.OrdinalIgnoreCase) && item.DegreeText == "0");
    }

    [Fact]
    public void CreateDrafts_ShouldInferUniqueWearPeriodFromCatalog_WhenTextHasNoWearPeriod()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("骞存姏", "缁樹笘绾辫摑", "100", "YEAR-001"),
            CreateCatalogEntry("骞存姏", "缁樹笘绾辫摑", "375", "YEAR-002")
        });

        var text = """
                   lenspop
                   缁樹笘绾辫摑 100/100
                   缁樹笘绾辫摑 375/375
                   灏忓挬 13287852759
                   灞变笢鐪佸▉娴峰競鐜繝鍖鸿礉鍗″皵鐗硅矾91鍙?
                   """;

        var drafts = factory.CreateDrafts(text, snapshot, null, out _);

        var draft = Assert.Single(drafts);
        Assert.Equal(2, draft.Items.Count);
        Assert.All(draft.Items, item => Assert.Equal("骞存姏", item.WearPeriod));
    }

    [Fact]
    public void CreateDrafts_ShouldDefaultLenspopWithoutExplicitPeriod_ToHalfYear_WhenCatalogHasMultiplePeriodsForSameModel()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("骞存姏", "缁樹笘绾辫摑", "100", "YEAR-001"),
            CreateCatalogEntry("鍗婂勾鎶?, "缁樹笘绾辫摑", "100", "HALF-001")
        });

        var text = """
                   lenspop
                   缁樹笘绾辫摑 100/100
                   灏忓挬 13287852759
                   灞变笢鐪佸▉娴峰競鐜繝鍖鸿礉鍗″皵鐗硅矾91鍙?
                   """;

        var drafts = factory.CreateDrafts(text, snapshot, null, out _);

        var draft = Assert.Single(drafts);
        var item = Assert.Single(draft.Items);
        Assert.Equal("鍗婂勾鎶?, item.WearPeriod);
    }

    [Fact]
    public void CreateDrafts_ShouldMapTrialAliasToConfiguredWearPeriod()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("鏃ユ姏2鐗?, "娴佸績鐝犺摑缁?, "550", "DAY-001")
        });
        snapshot.WearPeriods = new List<LookupValueRow>
        {
            new() { Value = "鏃ユ姏2鐗? }
        };
        snapshot.WearPeriodMappings = new List<WearPeriodMappingRow>
        {
            new() { Alias = "璇曟埓鐗?, WearPeriod = "鏃ユ姏2鐗? }
        };

        var text = """
                   lenspop鏃ユ姏涓ょ墖瑁?
                   璇曟埓鐗?
                   娴佸績鐝犺摑缁?50
                   """;

        var drafts = factory.CreateDrafts(text, snapshot, null, out _);

        var draft = Assert.Single(drafts);
        var item = Assert.Single(draft.Items);
        Assert.Equal("鏃ユ姏2鐗?, item.WearPeriod);
    }
    [Fact]
    public void CreateDrafts_ShouldPreferItemExplicitWearPeriod_OverOrderLevelDailyHeader()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "流萤森深蓝", "375", "DAY-001"),
            CreateCatalogEntry("半年抛", "三勾玉", "0", "HALF-000"),
            CreateCatalogEntry("半年抛", "三勾玉", "100", "HALF-100"),
            CreateCatalogEntry("半年抛", "三勾玉", "200", "HALF-200")
        });

        var text = """
                   lenspop日抛两片装
                   流萤森深蓝
                   以上全为375度 共1副
                   半年抛三勾玉0 半年抛三勾玉100 半年抛三勾玉200
                   许艺馨 19506782905 山东省枣庄市滕州市 善国北路君瑞城卡米Cosplay自拍馆
                   """;

        var drafts = factory.CreateDrafts(text, snapshot, null, out _);

        var draft = Assert.Single(drafts);
        var halfYearItems = draft.Items
            .Where(item => item.SourceText.Contains("三勾玉", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(3, halfYearItems.Count);
        Assert.All(halfYearItems, item => Assert.Equal("半年抛", item.WearPeriod));
        Assert.Contains(halfYearItems, item => item.DegreeText == "0");
        Assert.Contains(halfYearItems, item => item.DegreeText == "100");
        Assert.Contains(halfYearItems, item => item.DegreeText == "200");
    }
    [Fact]
    public void CreateDraftsInBatches_ShouldInvokeCallbackPerBatch()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("鍗婂勾鎶?, "缁樹笘绾辫摑", "100", "HALF-001")
        });

        var text = """
                   鐢?13800000001 骞夸笢鐪佹繁鍦冲競鍗楀北鍖虹鎶€鍥?
                   缁樹笘绾辫摑 100/100
                   涔?13800000002 骞夸笢鐪佹繁鍦冲競绂忕敯鍖烘繁鍗楀ぇ閬?
                   缁樹笘绾辫摑 100/100
                   涓?13800000003 骞夸笢鐪佹繁鍦冲競缃楁箹鍖轰汉姘戝崡璺?
                   缁樹笘绾辫摑 100/100
                   """;

        var batches = new List<IReadOnlyList<OrderDraft>>();
        var drafts = factory.CreateDraftsInBatches(text, snapshot, null, 1, batch => batches.Add(batch), out _);

        Assert.Equal(3, drafts.Count);
        Assert.Equal(3, batches.Count);
        Assert.All(batches, batch => Assert.Single(batch));
        Assert.All(batches.SelectMany(batch => batch), draft => Assert.False(string.IsNullOrWhiteSpace(draft.OrderNumber)));
        Assert.Equal(drafts.Count, drafts.Select(draft => draft.OrderNumber).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void CreateDrafts_ShouldGenerateDifferentOrderNumbers_ForSameOperatorAcrossRepeatedParses()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("HALF", "BLUE", "100", "HALF-001")
        });
        var account = new UserAccountRow
        {
            LoginName = "tester",
            DisplayName = "tester",
            ErpId = "ERP001"
        };

        var text = """
                   Zhang 13800000001 Shenzhen Nanshan
                   BLUE 100/100
                   """;

        var first = Assert.Single(factory.CreateDrafts(text, snapshot, account, out _));
        var second = Assert.Single(factory.CreateDrafts(text, snapshot, account, out _));

        Assert.NotEqual(first.OrderNumber, second.OrderNumber);
    }

    private static WorkflowSettingsSnapshot BuildSnapshot(IEnumerable<ProductCatalogEntry> catalog)
    {
        return new WorkflowSettingsSnapshot
        {
            RuleSet = ParserRuleSet.CreateDefault(),
            WearPeriods = new List<LookupValueRow>
            {
                new() { Value = "鏃ユ姏2鐗? },
                new() { Value = "鏃ユ姏10鐗? },
                new() { Value = "鍗婂勾鎶? },
                new() { Value = "骞存姏" },
                new() { Value = "璇曟埓鐗? }
            },
            WearPeriodMappings = new List<WearPeriodMappingRow>
            {
                new() { Alias = "鏃ユ姏", WearPeriod = "鏃ユ姏2鐗? },
                new() { Alias = "鍗婂勾鎶?, WearPeriod = "鍗婂勾鎶? },
                new() { Alias = "骞存姏", WearPeriod = "骞存姏" },
                new() { Alias = "璇曟埓", WearPeriod = "璇曟埓鐗? }
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

