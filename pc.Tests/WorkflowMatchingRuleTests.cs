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
    public void RefreshItem_ShouldPromoteToExact_WhenShortModelAliasAndDegreeUniquelyIdentifySku()
    {
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "云隙微光粉", "300", "SKU-YXWG-P-300"),
            CreateCatalogEntry("日抛2片", "云隙微光灰", "300", "SKU-YXWG-H-300"),
            CreateCatalogEntry("日抛2片", "云隙微光棕", "0", "SKU-YXWG-Z-0")
        });

        var item = new OrderItemDraft
        {
            ProductName = "隙微光灰",
            DegreeText = "300",
            SourceText = "隙微光灰两幅300"
        };

        resolver.RefreshItem(item, snapshot);

        Assert.Equal("SKU-YXWG-H-300", item.ProductCode);
        Assert.Equal("Exact", item.ProductMatchState);
        Assert.True(item.ProductCodeConfirmed);
    }

    [Fact]
    public void RefreshItem_ShouldPromoteToExact_WhenConcreteColorAndDegreeAreUnique_WithinSameSeries()
    {
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "云隙微光粉", "300", "SKU-YXWG-P-300"),
            CreateCatalogEntry("日抛2片", "云隙微光灰", "300", "SKU-YXWG-H-300"),
            CreateCatalogEntry("日抛2片", "云隙微光棕", "300", "SKU-YXWG-Z-300")
        });

        var item = new OrderItemDraft
        {
            ProductName = "云隙微光粉",
            DegreeText = "300",
            SourceText = "云隙微光粉两幅300"
        };

        resolver.RefreshItem(item, snapshot);

        Assert.Equal("SKU-YXWG-P-300", item.ProductCode);
        Assert.Equal("Exact", item.ProductMatchState);
        Assert.True(item.ProductCodeConfirmed);
    }

    [Fact]
    public void RefreshItem_ShouldPromoteToExact_WhenFamilyAndDegreeUniquelyIdentifySku_EvenIfWearPeriodIsNoisy()
    {
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "云隙微光灰", "300", "SKU-YXWG-H-300"),
            CreateCatalogEntry("日抛2片", "云隙微光灰", "400", "SKU-YXWG-H-400"),
            CreateCatalogEntry("日抛2片", "云隙微光粉", "300", "SKU-YXWG-P-300")
        });

        var item = new OrderItemDraft
        {
            ProductName = "隙微光灰",
            WearPeriod = "试戴片",
            DegreeText = "300",
            SourceText = "隙微光灰两幅300"
        };

        resolver.RefreshItem(item, snapshot);

        Assert.Equal("SKU-YXWG-H-300", item.ProductCode);
        Assert.Equal("Exact", item.ProductMatchState);
        Assert.True(item.ProductCodeConfirmed);
    }

    [Fact]
    public void RefreshItem_ShouldNotPromoteToExact_WhenExplicitWearPeriodIsWrong()
    {
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "筑梦蓝", "100", "SKU-DAILY-DREAM-BLUE-100")
        });

        var item = new OrderItemDraft
        {
            ProductName = "筑梦蓝",
            WearPeriod = "年抛",
            DegreeText = "100",
            SourceText = "年抛筑梦蓝100度"
        };

        resolver.RefreshItem(item, snapshot);

        Assert.Equal("SKU-DAILY-DREAM-BLUE-100", item.ProductCode);
        Assert.NotEqual("Exact", item.ProductMatchState);
        Assert.False(item.ProductCodeConfirmed);
    }

    [Fact]
    public void RefreshItem_ShouldPromoteToExact_WhenDailyWearTextIsBroadButFamilyAndDegreeAreUnique()
    {
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "筑梦蓝", "100", "SKU-DAILY-DREAM-BLUE-100"),
            CreateCatalogEntry("日抛2片", "筑梦蓝", "200", "SKU-DAILY-DREAM-BLUE-200")
        });

        var item = new OrderItemDraft
        {
            ProductName = "筑梦蓝",
            DegreeText = "100",
            SourceText = "日抛筑梦蓝100度"
        };

        resolver.RefreshItem(item, snapshot);

        Assert.Equal("SKU-DAILY-DREAM-BLUE-100", item.ProductCode);
        Assert.Equal("Exact", item.ProductMatchState);
        Assert.True(item.ProductCodeConfirmed);
    }

    [Fact]
    public void RefreshItem_ShouldPromoteToExact_WhenTenPieceDailyPrefixContainsExtraNumericPackInfo()
    {
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛10片", "游仙红", "0", "SKU-10P-YXH-0"),
            CreateCatalogEntry("日抛2片", "游仙红", "0", "SKU-2P-YXH-0")
        });

        var item = new OrderItemDraft
        {
            ProductName = "日抛10片游仙红0",
            SourceText = "日抛10片游仙红0"
        };

        resolver.RefreshItem(item, snapshot);

        Assert.Equal("SKU-10P-YXH-0", item.ProductCode);
        Assert.Equal("Exact", item.ProductMatchState);
        Assert.True(item.ProductCodeConfirmed);
    }

    [Fact]
    public void CreateDrafts_AndRefreshDraft_ShouldTreatPackPieceSuffixAsPackaging_NotStrictTenPieceWear()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "笼中梦红", "0", "SKU-CAGE-RED-0"),
            CreateCatalogEntry("日抛2片", "笼中梦红", "100", "SKU-CAGE-RED-100")
        });

        var drafts = factory.CreateDrafts(
            """
            LEEA 笼中梦红（日抛）*10片
            LEEA 笼中梦红（日抛）*10片
            LEEA 笼中梦红（日抛）*10片
            LEEA 笼中梦红（日抛）*10片
            江小姐4.11號   17878213891
            广东省珠海市香洲区拱北港三路3号中环大厦3樓貨架
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);
        Assert.Equal(4, draft.Items.Count);
        Assert.All(draft.Items, item => Assert.Equal("1", item.QuantityText));

        resolver.RefreshDraft(draft, snapshot);

        Assert.All(draft.Items, item =>
        {
            Assert.Equal("0", item.DegreeText);
            Assert.Equal("日抛2片", item.WearPeriod);
            Assert.Equal("SKU-CAGE-RED-0", item.ProductCode);
            Assert.Equal("Exact", item.ProductMatchState);
        });
    }

    [Fact]
    public void CreateDrafts_AndRefreshDraft_ShouldPreferDailyTwoPiece_WhenPeriodIsMissingButOnlyPackSizeDiffers()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "游仙红", "500", "SKU-2P-YXH-500"),
            CreateCatalogEntry("日抛10片", "游仙红", "500", "SKU-10P-YXH-500"),
            CreateCatalogEntry("日抛2片", "玛瑙冰绿", "500", "SKU-MNLG-500")
        });

        var drafts = factory.CreateDrafts(
            """
            灵隙金/红/绿/棕/紫/灰各来3副0度
            玛瑙冰绿500度一副
            游仙红500度一副

            遇仙大王15757676776
            浙江省台州市温岭市新河镇六闸潮六闸272号
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);
        resolver.RefreshDraft(draft, snapshot);

        var youxianRed = Assert.Single(draft.Items, item => item.SourceText.Contains("游仙红500", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("500", youxianRed.DegreeText);
        Assert.Equal("SKU-2P-YXH-500", youxianRed.ProductCode);
        Assert.Equal("Exact", youxianRed.ProductMatchState);
    }

    [Fact]
    public void RefreshItem_ShouldPromoteToExact_WhenConcatenatedHalfYearModelContainsExtraTerms()
    {
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("半年抛", "次元梦境Pro深蓝", "400", "SKU-HY-CYMJ-PRO-SL-400"),
            CreateCatalogEntry("半年抛", "次元梦境Pro深蓝", "450", "SKU-HY-CYMJ-PRO-SL-450")
        });

        var item = new OrderItemDraft
        {
            ProductName = "半年抛次元梦境Pro深蓝400",
            SourceText = "半年抛次元梦境Pro深蓝400 赠品"
        };

        resolver.RefreshItem(item, snapshot);

        Assert.Equal("SKU-HY-CYMJ-PRO-SL-400", item.ProductCode);
        Assert.Equal("Exact", item.ProductMatchState);
        Assert.True(item.ProductCodeConfirmed);
    }

    [Fact]
    public void CreateDrafts_AndRefreshDraft_ShouldPromoteShortModelAliasToExact_FromRawOrderText()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "云隙微光粉", "300", "SKU-YXWG-P-300"),
            CreateCatalogEntry("日抛2片", "云隙微光灰", "300", "SKU-YXWG-H-300"),
            CreateCatalogEntry("日抛2片", "云隙微光棕", "0", "SKU-YXWG-Z-0")
        });

        var drafts = factory.CreateDrafts(
            """
            品牌：LEEA莉亚
            款式：云隙微光粉两幅300，隙微光灰两幅300

            王甜蜜
            13956177357
            安徽省芜湖市鸠江区官陡街道城市之光第四街区2栋一元401
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);

        resolver.RefreshDraft(draft, snapshot);

        var grayItem = Assert.Single(draft.Items, item => item.SourceText.Contains("隙微光灰", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("SKU-YXWG-H-300", grayItem.ProductCode);
        Assert.Equal("Exact", grayItem.ProductMatchState);
        Assert.True(grayItem.ProductCodeConfirmed);
    }

    [Fact]
    public void CreateDrafts_AndRefreshDraft_ShouldExactMatchConcreteColorItems_WithinSameSeries()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "云隙微光粉", "300", "SKU-YXWG-P-300"),
            CreateCatalogEntry("日抛2片", "云隙微光灰", "300", "SKU-YXWG-H-300"),
            CreateCatalogEntry("日抛2片", "云隙微光棕", "300", "SKU-YXWG-Z-300"),
            CreateCatalogEntry("日抛2片", "盈月海蓝", "300", "SKU-YYHL-300")
        });

        var drafts = factory.CreateDrafts(
            """
            品牌：LEEA莉亚
            款式：云隙微光粉两幅300，隙微光灰两幅300，盈月海蓝两幅300

            王甜蜜
            13956177357
            安徽省芜湖市鸠江区官陡街道城市之光第四街区2栋一元401
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);

        resolver.RefreshDraft(draft, snapshot);

        var pinkItem = Assert.Single(draft.Items, item => item.SourceText.Contains("云隙微光粉", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("SKU-YXWG-P-300", pinkItem.ProductCode);
        Assert.Equal("Exact", pinkItem.ProductMatchState);
        Assert.True(pinkItem.ProductCodeConfirmed);

        var grayItem = Assert.Single(draft.Items, item => item.SourceText.Contains("隙微光灰", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("SKU-YXWG-H-300", grayItem.ProductCode);
        Assert.Equal("Exact", grayItem.ProductMatchState);
        Assert.True(grayItem.ProductCodeConfirmed);

        var blueItem = Assert.Single(draft.Items, item => item.SourceText.Contains("盈月海蓝", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("SKU-YYHL-300", blueItem.ProductCode);
        Assert.Equal("Exact", blueItem.ProductMatchState);
        Assert.True(blueItem.ProductCodeConfirmed);
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

    [Fact]
    public void CreateDrafts_AndRefreshDraft_ShouldExpandEnumeratedVariantsUsingSourceFamilyPrefix()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "流萤森深蓝", "375", "SKU-LYS-SL-375"),
            CreateCatalogEntry("日抛2片", "流萤森玫红", "375", "SKU-LYS-MH-375"),
            CreateCatalogEntry("日抛2片", "次元梦境pro蓝", "375", "SKU-CY-L-375"),
            CreateCatalogEntry("日抛2片", "次元梦境pro青", "375", "SKU-CY-Q-375"),
            CreateCatalogEntry("日抛2片", "次元梦境pro灰", "375", "SKU-CY-H-375")
        });

        var drafts = factory.CreateDrafts(
            """
            lenspop日抛两片装
            流萤森深蓝
            流萤森玫红
            次元梦境pro蓝x1、青x1、灰x1（共三款pro）
            以上全为375度 共5副

            许艺馨 19506782905 山东省枣庄市滕州市 善国北路君瑞城卡米Cosplay自拍馆
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);
        var summary = string.Join(" | ", draft.Items.Select(item => $"{item.ProductName}:{item.SourceText}:{item.DegreeText}:{item.QuantityText}"));
        Assert.True(draft.Items.Count == 5, summary);

        resolver.RefreshDraft(draft, snapshot);

        Assert.Contains(draft.Items, item => item.ProductCode == "SKU-LYS-SL-375");
        Assert.Contains(draft.Items, item => item.ProductCode == "SKU-LYS-MH-375");
        Assert.Contains(draft.Items, item => item.ProductCode == "SKU-CY-L-375");
        Assert.Contains(draft.Items, item => item.ProductCode == "SKU-CY-Q-375");
        Assert.Contains(draft.Items, item => item.ProductCode == "SKU-CY-H-375");

        var cyanItem = Assert.Single(draft.Items, item => item.ProductCode == "SKU-CY-Q-375");
        Assert.Contains("次元梦境", cyanItem.ProductName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("青", cyanItem.ProductName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("375", cyanItem.DegreeText);
    }

    [Fact]
    public void CreateDrafts_ShouldExpandEnumeratedVariantsIntoConcreteProductNames()
    {
        var factory = new OrderDraftFactory();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "次元梦境pro蓝", "375", "SKU-CY-L-375"),
            CreateCatalogEntry("日抛2片", "次元梦境pro青", "375", "SKU-CY-Q-375"),
            CreateCatalogEntry("日抛2片", "次元梦境pro灰", "375", "SKU-CY-H-375")
        });

        var drafts = factory.CreateDrafts(
            """
            lenspop日抛两片装
            次元梦境pro蓝x1、青x1、灰x1（共三款pro）
            以上全为375度 共3副
            张三 13800138000 浙江省杭州市余杭区xx路88号
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);
        var summary = string.Join(" | ", draft.Items.Select(item => $"{item.ProductName}:{item.SourceText}:{item.DegreeText}:{item.QuantityText}"));

        Assert.Contains(draft.Items, item => string.Equals(item.ProductName, "次元梦境pro蓝", StringComparison.OrdinalIgnoreCase));
        Assert.True(draft.Items.Any(item => string.Equals(item.ProductName, "次元梦境pro青", StringComparison.OrdinalIgnoreCase)), summary);
        Assert.True(draft.Items.Any(item => string.Equals(item.ProductName, "次元梦境pro灰", StringComparison.OrdinalIgnoreCase)), summary);
    }

    [Fact]
    public void CreateDrafts_AndRefreshDraft_ShouldTreatDailyTrialHeaderAsLocalDailyHintOnly()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("半年抛", "次元梦境Pro棕", "0", "SKU-HALF-PRO-BROWN-0"),
            CreateCatalogEntry("日抛2片", "光晕灰", "225", "SKU-DAILY-HALO-GRAY-225"),
            CreateCatalogEntry("半年抛", "光晕灰", "225", "SKU-HALF-HALO-GRAY-225")
        });

        var drafts = factory.CreateDrafts(
            """
            品牌：LEEA莉亚
            次元梦境Pro棕0
            lenspop日抛试戴片
            光晕灰225度两幅

            王甜蜜
            13956177357
            安徽省芜湖市鸠江区官陡街道城市之光第四街区2栋一元401
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);
        Assert.Equal(2, draft.Items.Count);

        resolver.RefreshDraft(draft, snapshot);

        var proBrown = Assert.Single(draft.Items, item => item.SourceText.Contains("次元梦境Pro棕", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("半年抛", proBrown.WearPeriod);
        Assert.Equal("SKU-HALF-PRO-BROWN-0", proBrown.ProductCode);

        var haloGray = Assert.Single(draft.Items, item => item.SourceText.Contains("光晕灰", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("日抛2片", haloGray.WearPeriod);
        Assert.Equal("SKU-DAILY-HALO-GRAY-225", haloGray.ProductCode);
        Assert.False(haloGray.IsTrial);
    }

    [Fact]
    public void CreateDrafts_AndRefreshDraft_ShouldTreatOrderLevelDailyTrialBrandAsDailyExactMatches()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "星辰泪青", "0", "SKU-DAILY-TEAR-CYAN-0"),
            CreateCatalogEntry("半年抛", "星辰泪青", "0", "SKU-HALF-TEAR-CYAN-0"),
            CreateCatalogEntry("日抛2片", "次元梦境pro黄", "300", "SKU-DAILY-PRO-YELLOW-300"),
            CreateCatalogEntry("半年抛", "次元梦境pro黄", "300", "SKU-HALF-PRO-YELLOW-300"),
            CreateCatalogEntry("日抛2片", "盈月海蓝", "600", "SKU-DAILY-MOON-BLUE-600"),
            CreateCatalogEntry("半年抛", "盈月海蓝", "600", "SKU-HALF-MOON-BLUE-600")
        });

        var drafts = factory.CreateDrafts(
            """
            品牌：lenspop日抛试戴片
            颜色：
            星辰泪青 0度
            次元梦境pro黄 300度
            盈月海蓝 600度
            数量：3副
            姓名：菜菜
            电话：15573155116
            地址：湖南省长沙市天心区桂花坪街道中豹塘196号家属区4栋101菜鸟驿站
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);
        Assert.Equal(3, draft.Items.Count);

        resolver.RefreshDraft(draft, snapshot);

        Assert.All(draft.Items, item =>
        {
            Assert.Equal("日抛2片", item.WearPeriod);
            Assert.Equal("Exact", item.ProductMatchState);
        });
        Assert.DoesNotContain(draft.Items, item => item.ProductCode.StartsWith("SKU-HALF-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(draft.Items, item => item.ProductCode == "SKU-DAILY-TEAR-CYAN-0");
        Assert.Contains(draft.Items, item => item.ProductCode == "SKU-DAILY-PRO-YELLOW-300");
        Assert.Contains(draft.Items, item => item.ProductCode == "SKU-DAILY-MOON-BLUE-600");
    }

    [Fact]
    public void CreateDrafts_AndRefreshDraft_ShouldSplitChainedInlineItems_AndExactMatchBoth()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "游仙红", "400", "SKU-YXH-400"),
            CreateCatalogEntry("日抛2片", "星辰泪灰", "400", "SKU-XCLH-400")
        });

        var drafts = factory.CreateDrafts(
            """
            下单
            商品信息:513区LENSPOP 日抛
            客户备注:游仙红两副400星辰泪灰 400
            瑞瑞 15601864170
            浙江省杭州市余杭区仓前街道滨江枫秀云庭7幢1单元302室
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);

        resolver.RefreshDraft(draft, snapshot);

        var youxian = Assert.Single(draft.Items, item => item.ProductName.Contains("游仙红", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("400", youxian.DegreeText);
        Assert.Equal("SKU-YXH-400", youxian.ProductCode);
        Assert.Equal("Exact", youxian.ProductMatchState);

        var tearsGray = Assert.Single(draft.Items, item => item.ProductName.Contains("星辰泪灰", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("400", tearsGray.DegreeText);
        Assert.Equal("SKU-XCLH-400", tearsGray.ProductCode);
        Assert.Equal("Exact", tearsGray.ProductMatchState);
    }

    [Fact]
    public void CreateDrafts_AndRefreshDraft_ShouldPropagateLeadingDegreeAcrossAdjacentModels()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "塞壬泉紫", "375", "SKU-SRQZ-375"),
            CreateCatalogEntry("日抛2片", "星辰泪金棕", "375", "SKU-XCLJZ-375"),
            CreateCatalogEntry("日抛2片", "玛瑙冰蓝", "375", "SKU-MNBL-375"),
            CreateCatalogEntry("日抛2片", "光晕棕", "375", "SKU-GYZ-375"),
            CreateCatalogEntry("日抛2片", "次元梦境蓝", "0", "SKU-CYMJL-0")
        });

        var drafts = factory.CreateDrafts(
            """
            下单：
            10549834
            收货人：呆呆 19947607704
            详细地址：湖北省武汉市汉阳区葛洲坝紫郡兰园8幢1303
            订单备注：塞壬泉紫375度 星辰泪金棕 玛瑙冰蓝 光晕棕 次元梦镜蓝0度
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);
        var beforeRefreshSummary = string.Join(" | ", draft.Items.Select(item =>
            $"{item.SourceText}->{item.ProductName}:{item.DegreeText}:{item.WearPeriod}"));
        resolver.RefreshDraft(draft, snapshot);
        var summary = $"{beforeRefreshSummary} || {string.Join(" | ", draft.Items.Select(item =>
            $"{item.SourceText}->{item.ProductName}:{item.DegreeText}:{item.ProductMatchState}:{item.ProductWorkflowDetail}"))}";

        Assert.True(draft.Items.Any(item => item.ProductName.Contains("塞壬泉紫", StringComparison.OrdinalIgnoreCase) && item.DegreeText == "375" && item.ProductMatchState == "Exact"), summary);
        Assert.True(draft.Items.Any(item => item.ProductName.Contains("星辰泪金棕", StringComparison.OrdinalIgnoreCase) && item.DegreeText == "375" && item.ProductMatchState == "Exact"), summary);
        Assert.True(draft.Items.Any(item => item.ProductName.Contains("玛瑙冰蓝", StringComparison.OrdinalIgnoreCase) && item.DegreeText == "375" && item.ProductMatchState == "Exact"), summary);
        Assert.True(draft.Items.Any(item => item.ProductName.Contains("光晕棕", StringComparison.OrdinalIgnoreCase) && item.DegreeText == "375" && item.ProductMatchState == "Exact"), summary);
        Assert.True(draft.Items.Any(item => item.ProductName.Contains("次元梦境蓝", StringComparison.OrdinalIgnoreCase) && item.DegreeText == "0" && item.ProductMatchState == "Exact"), summary);
    }

    [Fact]
    public void CreateDrafts_AndRefreshDraft_ShouldNormalizeProSuffixAfterColor_AndExactMatch()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("半年抛", "次元梦境pro深蓝", "600", "SKU-PRO-DEEPBLUE-600"),
            CreateCatalogEntry("半年抛", "次元梦境pro绿", "600", "SKU-PRO-GREEN-600")
        });

        var drafts = factory.CreateDrafts(
            """
            lenspop十副
            次元梦境深蓝pro600
            次元梦境绿pro600度
            小鱼 18815123904 浙江省温州市乐清市万翁北路88号2号楼后面9号门
            190
            """,
            snapshot,
            null,
            out _);

        var draft = Assert.Single(drafts);
        resolver.RefreshDraft(draft, snapshot);

        Assert.Contains(draft.Items, item => item.ProductCode == "SKU-PRO-DEEPBLUE-600" && item.ProductMatchState == "Exact");
        Assert.Contains(draft.Items, item => item.ProductCode == "SKU-PRO-GREEN-600" && item.ProductMatchState == "Exact");
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
