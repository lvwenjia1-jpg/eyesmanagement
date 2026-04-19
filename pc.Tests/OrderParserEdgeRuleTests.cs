using OrderTextTrainer.Core.Models;
using OrderTextTrainer.Core.Services;
using WpfApp11;
using Xunit;

namespace pc.Tests;

public sealed class OrderParserEdgeRuleTests
{
    [Fact]
    public void Parse_ShouldKeepTrailingPowerQuantity_AsSameProductItem()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            Lenspop半年抛
            次元梦境深蓝（左眼500 右眼400）*1
            次元梦境pro紫（左眼500 右眼400）*1 500*1
            """,
            ParserRuleSet.CreateDefault(),
            Catalog("次元梦境深蓝", "次元梦境pro紫"));

        var order = Assert.Single(result.Orders);
        Assert.Contains(order.Items, item => item.ProductName == "次元梦境深蓝" && item.PowerSummary == "500");
        Assert.Equal(2, order.Items.Count(item =>
            string.Equals(item.ProductName, "次元梦境pro紫", StringComparison.OrdinalIgnoreCase) &&
            item.PowerSummary == "500"));
    }

    [Fact]
    public void Parse_ShouldRecognizeChineseQuantityUnits_AndKeepModelNameClean()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            品牌：LEEA莉亚
            款式：云隙微光粉两幅300，隙微光灰两幅300，盈月海蓝两幅300
            王甜蜜
            13956177357
            安徽省芜湖市鸠江区官陡街道城市之光第四街区2栋一元401
            """,
            ParserRuleSet.CreateDefault());

        var order = Assert.Single(result.Orders);
        Assert.Collection(order.Items,
            item =>
            {
                Assert.Equal("云隙微光粉", item.ProductName);
                Assert.Equal(2, item.Quantity);
                Assert.Equal("300", item.PowerSummary);
            },
            item =>
            {
                Assert.Equal("隙微光灰", item.ProductName);
                Assert.Equal(2, item.Quantity);
                Assert.Equal("300", item.PowerSummary);
            },
            item =>
            {
                Assert.Equal("盈月海蓝", item.ProductName);
                Assert.Equal(2, item.Quantity);
                Assert.Equal("300", item.PowerSummary);
            });
    }

    [Fact]
    public void Parse_ShouldTreatNoDegreeAsZero_AndIgnorePostalCode()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            lenspop日抛
            星辰泪蓝无度数一副
            收货人: 卞薪雅
            手机号码: 18049924027
            所在地区: 上海市上海市奉贤区奉浦大道123号上海商学院东方美谷大道6333号
            邮政编码: 201499
            """,
            ParserRuleSet.CreateDefault(),
            Catalog("星辰泪蓝"));

        var order = Assert.Single(result.Orders);
        var item = Assert.Single(order.Items);
        Assert.Equal("0", item.PowerSummary);
        Assert.DoesNotContain(result.UnknownSegments, segment => segment.Contains("201499", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_ShouldPreferExplicitDegreeLabel_OverDailyPackNumber()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            Lenspop灵隙金14.5mm【日抛4】度数：0
            河南省信阳市新县新集镇将军路职业高中南刘应满米粉加工厂, 金玲慧, 18837631636-9285
            """,
            ParserRuleSet.CreateDefault(),
            Catalog("灵隙金"));

        var order = Assert.Single(result.Orders);
        var item = Assert.Single(order.Items);
        Assert.Equal("0", item.PowerSummary);
    }

    [Fact]
    public void Parse_ShouldComposeExplicitAddress_WithoutDuplicatingRegionLabels()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            lenspop
            次元梦境红550
            收货人: 徐, 手机号码: 15173013712, 所在地区: 湖南省岳阳市岳阳楼区金凤桥管理处, 详细地址: 湖南省岳阳市岳阳楼区金凤桥管理处连接线五公里圆通点
            """,
            ParserRuleSet.CreateDefault(),
            Catalog("次元梦境红"));

        var order = Assert.Single(result.Orders);
        Assert.Equal("徐", order.CustomerName);
        Assert.Equal("15173013712", order.Phone);
        Assert.Equal("湖南省岳阳市岳阳楼区金凤桥管理处连接线五公里圆通点", order.Address);
    }

    [Fact]
    public void Parse_ShouldKeepUnknownTrailingModel_FromRemarkLine()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            下单：
            10549834
            收货人：呆呆 19947607704
            详细地址：湖北省武汉市汉阳区葛洲坝紫郡兰园8幢1303
            订单备注：塞壬泉紫375度 星辰泪金棕 玛瑙冰蓝 光晕棕 次元梦镜蓝0度
            """,
            ParserRuleSet.CreateDefault(),
            Catalog("塞壬泉紫", "星辰泪金棕", "玛瑙冰蓝", "光晕棕"));

        var order = Assert.Single(result.Orders);
        Assert.Contains(order.Items, item =>
            item.RawText.Contains("次元梦镜蓝", StringComparison.OrdinalIgnoreCase) &&
            item.PowerSummary == "0");
    }

    [Fact]
    public void Parse_ShouldHandleTrialBulkOrderWithoutTreatingQuantitySummaryAsItem()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            品牌：lenspop日抛试戴片
            颜色：
            星辰泪青 0度
            冰砂糖金棕 200度
            灵隙棕 100度
            灵隙棕 100度
            次元梦境蓝 100度
            流萤森深蓝 0度
            流萤森深蓝 0度
            流萤森深蓝 0度
            次元梦境蓝灰 0度
            次元梦境蓝灰 0度
            璃晶幻影金棕 0度
            璃晶幻影金棕 0度
            璃晶幻影金棕 0度
            次元梦境pro粉 0度
            次元梦境pro粉 0度
            次元梦境pro粉 0度
            次元梦境pro绿 0度
            次元梦境pro绿 0度
            次元梦境pro绿 0度
            灵隙灰 0度
            灵隙灰 0度
            灵隙红 0度
            灵隙红 0度
            流萤森玫红 150度
            次元梦境蓝灰 150度
            次元梦境茶棕 150度
            次元梦境pro灰 150度
            次元梦境pro灰 250度
            灵隙红 350度
            灵隙红 350度
            灵隙灰 0度
            灵隙红 0度
            灵隙红 0度
            冰砂糖蓝 0度
            冰砂糖蓝 0度
            游仙红 0度
            冰砂糖红 0度
            冰砂糖红 0度
            游仙红 400度
            次元梦境pro黄 300度
            次元梦境pro粉 300度
            灵隙绿 0度
            星辰泪灰 0度
            灵隙红 0度
            灵隙金 0度
            灵隙蓝紫 0度
            星辰泪金 0度
            次元梦境pro黄 0度
            次元梦境pro黄 0度
            次元梦境pro棕 0度
            次元梦境pro棕 0度
            次元梦境pro紫 0度
            次元梦境pro紫 0度
            盈月海蓝 600度
            灵隙红 400度
            灵隙红 400度
            灵隙灰 400度
            灵隙灰 400度
            灵隙金 400度
            灵隙金 400度
            数量：60副
            姓名：菜菜
            电话：15573155116
            地址：湖南省长沙市天心区桂花坪街道中豹塘196号家属区4栋101菜鸟驿站
            """,
            ParserRuleSet.CreateDefault());

        var order = Assert.Single(result.Orders);
        Assert.Equal(60, order.Items.Count);
        Assert.DoesNotContain(order.Items, item => (item.ProductName ?? string.Empty).Contains("数量", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(order.Items, item => item.ProductName == "盈月海蓝" && item.PowerSummary == "600");
        Assert.Equal(3, order.Items.Count(item => item.ProductName == "流萤森深蓝" && item.PowerSummary == "0"));
        Assert.Equal(2, order.Items.Count(item => item.ProductName == "灵隙金" && item.PowerSummary == "400"));
    }

    private static ProductCatalogEntry[] Catalog(params string[] names)
    {
        return names.Select(name => new ProductCatalogEntry { ProductName = name }).ToArray();
    }
}
