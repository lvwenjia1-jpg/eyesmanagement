using OrderTextTrainer.Core.Models;
using OrderTextTrainer.Core.Services;
using Xunit;

namespace pc.Tests;

public sealed class OrderTextParserTests
{
    [Fact]
    public void Parse_WithAddressPrefixedMixedTail_ShouldRecoverItem()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            ------------------------------------------------------

            彩潼拼订单        260412-127601231461930        LENSpop日抛新品竖瞳         高光定轴（灵隙棕）日抛2片装,0度         ×1        張/广东省 珠海市 斗门区 乾务镇鸿越科技园2号库@DX-FZ9-EJVCVSAJ#        /-

            -----------------------------------------------
            """,
            ParserRuleSet.CreateDefault());

        var order = Assert.Single(result.Orders);
        Assert.Equal("張", order.CustomerName);

        var item = Assert.Single(order.Items);
        Assert.Contains("灵隙棕", item.RawText);
        Assert.Equal("0", item.PowerSummary);
    }

    [Fact]
    public void Parse_WithTrailingPowerNearPhoneDigits_ShouldKeepOnlyRealItem()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            lenspop日抛两片装
            姓名：图图
            电话：13766593011
            地址：浙江省杭州市西湖区三墩镇振华路200号1009
            下单：
            流心珠蓝绿550
            ---------------
            缺货
            """,
            ParserRuleSet.CreateDefault(),
            Catalog("流心珠蓝绿"));

        var order = Assert.Single(result.Orders);
        var item = Assert.Single(order.Items);
        Assert.Equal("流心珠蓝绿", item.ProductName);
        Assert.Equal("550", item.PowerSummary);
        Assert.DoesNotContain(result.UnknownSegments, segment => segment.Contains("下单", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_WithOrderWideDegreeNote_ShouldApplyDegreeToAllItems()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            lenspop日抛两片装
            流萤森深蓝x1
            流萤森玫红x1
            次元梦境pro蓝x1
            以上全为375度 共3副
            许艺馨 19506782905 山东省枣庄市滕州市 善国北路君瑞城卡米Cosplay自拍馆
            """,
            ParserRuleSet.CreateDefault(),
            Catalog("流萤森深蓝", "流萤森玫红", "次元梦境pro蓝"));

        var order = Assert.Single(result.Orders);
        Assert.Equal(3, order.Items.Count);
        Assert.All(order.Items, item => Assert.Equal("375", item.PowerSummary));
        Assert.All(order.Items, item => Assert.Equal("375", item.LeftPower));
    }

    [Fact]
    public void Parse_WithMetadataNoise_ShouldIgnoreHeadersLinksAndPromoInfo()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            下单
            订单号: 10549956
            http://www.eyekin.cn/WeixinShop/member/M_OrderInfoCK.aspx?id=549956
            商品信息:513区LENSPOP 日抛 瞳物语 星 品上市 十盒 限时促销
            客户备注:次元梦境红0度 冰砂糖紫0度 次元梦境灰0
            游仙红300度三副
            次元梦境pro紫0度两副 光晕紫0度一副
            冰砂糖红375度2副 高光月球绿375度2副
            深空物语绿350一副，玛瑙冰金棕350一副，次元梦境pro棕350一副
            游仙红两副400星辰泪灰 400
            次元梦境pro蓝 750
            瑞瑞 15601864170
            浙江省 杭州市 余杭区 仓前街道滨江枫秀云庭7幢1单元302室
            ----------------------------------------------
            """,
            ParserRuleSet.CreateDefault(),
            Catalog(
                "次元梦境红",
                "冰砂糖紫",
                "次元梦境灰",
                "游仙红",
                "次元梦境pro紫",
                "光晕紫",
                "冰砂糖红",
                "高光月球绿",
                "深空物语绿",
                "玛瑙冰金棕",
                "次元梦境pro棕",
                "星辰泪灰",
                "次元梦境pro蓝"));

        var order = Assert.Single(result.Orders);
        Assert.Equal("瑞瑞", order.CustomerName);
        Assert.Equal("15601864170", order.Phone);
        Assert.Contains("浙江省杭州市余杭区", order.Address);

        Assert.Contains(order.Items, item => item.ProductName == "次元梦境红" && item.PowerSummary == "0");
        Assert.Contains(order.Items, item => item.ProductName == "游仙红" && item.PowerSummary == "300");
        Assert.Contains(order.Items, item => item.ProductName == "次元梦境pro蓝" && item.PowerSummary == "750");

        Assert.DoesNotContain(order.Items, item =>
            item.RawText.Contains("订单号", StringComparison.OrdinalIgnoreCase) ||
            item.RawText.Contains("http", StringComparison.OrdinalIgnoreCase) ||
            item.RawText.Contains("商品信息", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_WithUnknownStandaloneModels_ShouldKeepItemsInInputOrder()
    {
        var parser = new OrderTextParser();
        var result = parser.Parse(
            """
            lenspop日抛
            0度
            雾屿蓝紫
            朝雾金棕
            暮光粉
            空镜青
            收货人：呆呆 19947607704
            详细地址：湖北省武汉市汉阳区葛洲坝紫郡兰园8幢1303
            """,
            ParserRuleSet.CreateDefault());

        var order = Assert.Single(result.Orders);
        Assert.Collection(order.Items,
            item =>
            {
                Assert.Equal("雾屿蓝紫", item.ProductName);
                Assert.Equal("0", item.PowerSummary);
            },
            item =>
            {
                Assert.Equal("朝雾金棕", item.ProductName);
                Assert.Equal("0", item.PowerSummary);
            },
            item =>
            {
                Assert.Equal("暮光粉", item.ProductName);
                Assert.Equal("0", item.PowerSummary);
            },
            item =>
            {
                Assert.Equal("空镜青", item.ProductName);
                Assert.Equal("0", item.PowerSummary);
            });
    }

    private static ProductCatalogEntry[] Catalog(params string[] names)
    {
        return names.Select(name => new ProductCatalogEntry { ProductName = name }).ToArray();
    }
}
