using System.Text.Json;
using Xunit;

namespace WpfApp11.Tests;

public sealed class HupunB2cTradeUploaderTests
{
    [Fact]
    public void BuildTradeListQueryFields_ShouldUseDocumentedPagingAndFallbackTimeRange()
    {
        var now = new DateTime(2026, 4, 16, 12, 34, 56);
        var draft = new OrderDraft
        {
            OrderNumber = "ERP-QUERY-001"
        };

        var fields = HupunB2cTradeUploader.BuildTradeListQueryFieldsForTesting(draft, new UploadConfiguration(), now);

        Assert.Equal("1", fields["page"]);
        Assert.Equal("20", fields["limit"]);
        Assert.Equal("ERP-QUERY-001", fields["bill_code"]);
        Assert.Equal("2026-04-09 12:34:56", fields["create_time"]);
        Assert.Equal("2026-04-16 12:34:56", fields["end_time"]);
        Assert.DoesNotContain("page_no", fields.Keys);
        Assert.DoesNotContain("page_size", fields.Keys);
        Assert.DoesNotContain("trade_id", fields.Keys);
        Assert.DoesNotContain("shop_nick", fields.Keys);
    }

    [Fact]
    public void BuildSignedRequestFields_QueryMode_ShouldUseOfficialOpenApiSystemParameters()
    {
        var configuration = new UploadConfiguration
        {
            AppKey = "14318175737",
            Secret = "secret"
        };

        var requestFields = HupunB2cTradeUploader.BuildSignedRequestFieldsForTesting(
            configuration,
            new Dictionary<string, string>
            {
                ["page"] = "1",
                ["limit"] = "20",
                ["create_time"] = "2026-04-09 12:34:56",
                ["end_time"] = "2026-04-16 12:34:56"
            },
            queryMode: true,
            timestamp: "1776272163226");

        Assert.Equal("14318175737", requestFields["_app"]);
        Assert.Equal("1776272163", requestFields["_t"]);
        Assert.Matches("^[A-F0-9]{32}$", requestFields["_sign"]);
        Assert.DoesNotContain("_s", requestFields.Keys);
        Assert.DoesNotContain("app_key", requestFields.Keys);
        Assert.DoesNotContain("timestamp", requestFields.Keys);
        Assert.DoesNotContain("format", requestFields.Keys);
        Assert.DoesNotContain("sign", requestFields.Keys);
    }

    [Fact]
    public void BuildSignedRequestFields_QueryMode_ShouldMatchOfficialSignatureExample()
    {
        var configuration = new UploadConfiguration
        {
            AppKey = "3823532979",
            Secret = "ea5b29320cb3d15a9883c1fa4654bd02"
        };

        var requestFields = HupunB2cTradeUploader.BuildSignedRequestFieldsForTesting(
            configuration,
            new Dictionary<string, string>
            {
                ["page"] = "1",
                ["limit"] = "200",
                ["bill_code"] = "OFL20221212164119384010001,OFL20221212155533384009005",
                ["query_extend"] = "{\"query_relation_trade\":true,\"encry_address\":true}"
            },
            queryMode: true,
            timestamp: "1672213377000");

        Assert.Equal("1672213377", requestFields["_t"]);
        Assert.Equal("F5BB54ED438BE6780110A2E4A58FA0EF", requestFields["_sign"]);
    }

    [Fact]
    public void BuildTradePushFields_ShouldUseTradesPayload()
    {
        var now = new DateTime(2026, 4, 16, 21, 30, 45);
        var draft = new OrderDraft
        {
            DraftId = "DRAFT-001",
            OrderNumber = "ERP-PUSH-001",
            OperatorLoginName = "user1",
            OperatorErpId = "ERP001",
            ReceiverName = "receiver",
            ReceiverMobile = "13766593011",
            ReceiverAddress = "Zhejiang Hangzhou Xihu Gudun Road 1009",
            Remark = "memo"
        };
        draft.Items.Add(new OrderItemDraft
        {
            ProductCode = "SKU-001",
            ProductName = "Product 001",
            SpecCodeText = "SPEC-001",
            BarcodeText = "BAR-001",
            DegreeText = "550",
            QuantityText = "2"
        });

        var fields = HupunB2cTradeUploader.BuildTradePushFieldsForTesting(draft, new UploadConfiguration(), now);

        Assert.Single(fields);
        using var tradesDocument = JsonDocument.Parse(fields["trades"]);
        var trade = Assert.Single(tradesDocument.RootElement.EnumerateArray());
        Assert.Equal("2026-04-16 21:30:45", trade.GetProperty("create_time").GetString());
        Assert.Equal("2026-04-16 21:30:45", trade.GetProperty("modify_time").GetString());
        Assert.Equal("2026-04-16 20:30:45", trade.GetProperty("pay_time").GetString());
        Assert.Equal("ERP-PUSH-001", trade.GetProperty("trade_id").GetString());
        Assert.Equal("user1", trade.GetProperty("buyer").GetString());
        Assert.Equal("receiver", trade.GetProperty("receiver_name").GetString());
        Assert.Equal("13766593011", trade.GetProperty("receiver_mobile").GetString());
        Assert.Equal("memo", trade.GetProperty("seller_memo").GetString());
        Assert.Equal("ERP001", trade.GetProperty("sales_mobile").GetString());
        Assert.Equal(2, trade.GetProperty("status").GetInt32());
        Assert.False(trade.TryGetProperty("buyer_nick", out _));
        Assert.False(trade.TryGetProperty("trade_details", out _));

        var order = Assert.Single(trade.GetProperty("orders").EnumerateArray());
        Assert.Equal("SKU-001", order.GetProperty("item_id").GetString());
        Assert.Equal("SKU-001", order.GetProperty("item_code").GetString());
        Assert.Equal("SKU-001", order.GetProperty("item_title").GetString());
        Assert.Equal("ERP-PUSH-001-001", order.GetProperty("order_id").GetString());
        Assert.Equal(2, order.GetProperty("size").GetInt32());
        Assert.Equal(2, order.GetProperty("status").GetInt32());
    }

    [Fact]
    public void BuildTradePushFields_ShouldAllowCancelStatus()
    {
        var now = new DateTime(2026, 4, 16, 21, 30, 45);
        var draft = new OrderDraft
        {
            DraftId = "DRAFT-CANCEL-001",
            OrderNumber = "ERP-CANCEL-001",
            ReceiverName = "receiver",
            ReceiverAddress = "addr"
        };
        draft.Items.Add(new OrderItemDraft
        {
            ProductCode = "SKU-CANCEL-001",
            QuantityText = "1"
        });

        var fields = HupunB2cTradeUploader.BuildTradePushFieldsForTesting(
            draft,
            new UploadConfiguration(),
            now,
            HupunB2cTradeUploader.CancelUploadTradeStatus);

        using var tradesDocument = JsonDocument.Parse(fields["trades"]);
        var trade = Assert.Single(tradesDocument.RootElement.EnumerateArray());
        Assert.Equal(4, trade.GetProperty("status").GetInt32());
        var order = Assert.Single(trade.GetProperty("orders").EnumerateArray());
        Assert.Equal(4, order.GetProperty("status").GetInt32());
    }

    [Fact]
    public void BuildGoodsWithSpecListFullQueryFields_ShouldUseModifyTimeRangeAndPaging()
    {
        var modifyTime = new DateTime(2010, 1, 1, 0, 0, 0);
        var endTime = new DateTime(2026, 4, 23, 9, 8, 7);

        var fields = HupunB2cTradeUploader.BuildGoodsWithSpecListFullQueryFieldsForTesting(
            modifyTime,
            endTime,
            page: 3,
            limit: 500);

        Assert.Equal("2010-01-01 00:00:00", fields["modify_time"]);
        Assert.Equal("2026-04-23 09:08:07", fields["end_time"]);
        Assert.Equal("3", fields["page"]);
        Assert.Equal("200", fields["limit"]);
        Assert.DoesNotContain("spec_code", fields.Keys);
        Assert.DoesNotContain("item_code", fields.Keys);
        Assert.DoesNotContain("bar_code", fields.Keys);
    }

    [Fact]
    public void BuildTradePushFields_ShouldUseResolvedGoodsCodeWhenProductCodeMissing()
    {
        var now = new DateTime(2026, 4, 16, 21, 30, 45);
        var draft = new OrderDraft
        {
            DraftId = "DRAFT-002",
            OrderNumber = "ERP-PUSH-002",
            ReceiverName = "receiver",
            ReceiverAddress = "addr"
        };
        draft.Items.Add(new OrderItemDraft
        {
            SpecCodeText = "SPEC-002",
            ProductName = "Product 002",
            QuantityText = "1"
        });

        var fields = HupunB2cTradeUploader.BuildTradePushFieldsForTesting(draft, new UploadConfiguration(), now);

        using var tradesDocument = JsonDocument.Parse(fields["trades"]);
        var trade = Assert.Single(tradesDocument.RootElement.EnumerateArray());
        var order = Assert.Single(trade.GetProperty("orders").EnumerateArray());
        Assert.Equal("SPEC-002", order.GetProperty("item_id").GetString());
        Assert.Equal("SPEC-002", order.GetProperty("item_code").GetString());
        Assert.Equal("SPEC-002", order.GetProperty("item_title").GetString());
    }

    [Fact]
    public void BuildTradePushFields_ShouldKeepItemTitleUsingResolvedGoodsCode()
    {
        var now = new DateTime(2026, 4, 16, 21, 30, 45);
        var draft = new OrderDraft
        {
            DraftId = "DRAFT-003",
            OrderNumber = "ERP-PUSH-003",
            ReceiverName = "receiver",
            ReceiverAddress = "addr"
        };
        draft.Items.Add(new OrderItemDraft
        {
            ProductCode = "SKU-003",
            SpecCodeText = "SPEC-003",
            ProductName = "Product 003",
            QuantityText = "1"
        });

        var fields = HupunB2cTradeUploader.BuildTradePushFieldsForTesting(draft, new UploadConfiguration(), now);

        using var tradesDocument = JsonDocument.Parse(fields["trades"]);
        var trade = Assert.Single(tradesDocument.RootElement.EnumerateArray());
        var order = Assert.Single(trade.GetProperty("orders").EnumerateArray());
        Assert.Equal("SKU-003", order.GetProperty("item_id").GetString());
        Assert.Equal("SKU-003", order.GetProperty("item_code").GetString());
        Assert.Equal("SKU-003", order.GetProperty("item_title").GetString());
    }

    [Fact]
    public void BuildTradePushFields_ShouldFallbackToBarcodeWhenNoProductOrSpecCode()
    {
        var now = new DateTime(2026, 4, 16, 21, 30, 45);
        var draft = new OrderDraft
        {
            DraftId = "DRAFT-004",
            OrderNumber = "ERP-PUSH-004",
            ReceiverName = "receiver",
            ReceiverAddress = "addr"
        };
        draft.Items.Add(new OrderItemDraft
        {
            BarcodeText = "BAR-004",
            ProductName = "Product 004",
            QuantityText = "1"
        });

        var fields = HupunB2cTradeUploader.BuildTradePushFieldsForTesting(draft, new UploadConfiguration(), now);

        using var tradesDocument = JsonDocument.Parse(fields["trades"]);
        var trade = Assert.Single(tradesDocument.RootElement.EnumerateArray());
        var order = Assert.Single(trade.GetProperty("orders").EnumerateArray());
        Assert.Equal("BAR-004", order.GetProperty("item_id").GetString());
        Assert.Equal("BAR-004", order.GetProperty("item_code").GetString());
        Assert.Equal("BAR-004", order.GetProperty("item_title").GetString());
    }

    [Fact]
    public void BuildSignedRequestFields_UploadMode_ShouldUseOfficialOpenApiSystemParameters()
    {
        var configuration = new UploadConfiguration
        {
            AppKey = "14318175737",
            Secret = "secret"
        };

        var requestFields = HupunB2cTradeUploader.BuildSignedRequestFieldsForTesting(
            configuration,
            new Dictionary<string, string>
            {
                ["trades"] = "[{\"trade_id\":\"ERP-PUSH-001\",\"create_time\":\"2026-04-16 21:30:45\",\"modify_time\":\"2026-04-16 21:30:45\",\"receiver_name\":\"user\",\"receiver_address\":\"addr\",\"shop_nick\":\"shop\",\"status\":0,\"orders\":[{\"item_id\":\"ABC\",\"item_title\":\"ABC\",\"order_id\":\"ERP-PUSH-001-001\",\"size\":1}]}]"
            },
            queryMode: false,
            timestamp: "1776272163226");

        Assert.Equal("14318175737", requestFields["_app"]);
        Assert.Equal("1776272163", requestFields["_t"]);
        Assert.Matches("^[A-F0-9]{32}$", requestFields["_sign"]);
        Assert.DoesNotContain("app_key", requestFields.Keys);
        Assert.DoesNotContain("timestamp", requestFields.Keys);
        Assert.DoesNotContain("format", requestFields.Keys);
        Assert.DoesNotContain("sign", requestFields.Keys);
    }

    [Fact]
    public void IsBusinessSuccess_ShouldAcceptOpenTradeListCodeZero()
    {
        Assert.True(HupunB2cTradeUploader.IsBusinessSuccessForTesting("{\"code\":0,\"data\":[]}"));
        Assert.False(HupunB2cTradeUploader.IsBusinessSuccessForTesting("{\"code\":1005,\"data\":[]}"));
    }

    [Fact]
    public void IsBusinessSuccess_ShouldTreatNestedSuccessFalseAsFailure()
    {
        Assert.False(HupunB2cTradeUploader.IsBusinessSuccessForTesting(
            "{\"code\":0,\"data\":{\"success\":false,\"error_code\":\"5002\",\"error_msg\":\"shop mismatch\"}}"));
    }

    [Fact]
    public void BuildFriendlyMessage_ShouldSurfaceNestedShopMismatch()
    {
        var message = HupunB2cTradeUploader.BuildFriendlyMessageForTesting(
            new UploadConfiguration { AppKey = "T3864192136" },
            "{\"code\":0,\"data\":{\"success\":false,\"error_code\":\"5002\",\"error_msg\":\"订单推送的店铺信息不正确\"}}",
            queryMode: false);

        Assert.Contains("shop info mismatch", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildEndpointCandidates_QueryMode_ShouldPreferOpenApiHost()
    {
        var endpoints = HupunB2cTradeUploader.BuildEndpointCandidatesForTesting(
            "https://erp-open.hupun.com/api",
            "/erp/opentrade/list/trades",
            "open-api.hupun.com");

        Assert.NotEmpty(endpoints);
        Assert.Equal("https://open-api.hupun.com/api/erp/opentrade/list/trades", endpoints[0]);
        Assert.Contains("https://erp-open.hupun.com/api/erp/opentrade/list/trades", endpoints);
    }

    [Fact]
    public void BuildEndpointCandidates_QueryMode_ShouldResolveFromConfiguredUploadEndpoint()
    {
        var endpoints = HupunB2cTradeUploader.BuildEndpointCandidatesForTesting(
            "https://open-api.hupun.com/api/erp/b2c/trades/open",
            "/erp/opentrade/list/trades",
            "open-api.hupun.com");

        Assert.NotEmpty(endpoints);
        Assert.Equal("https://open-api.hupun.com/api/erp/opentrade/list/trades", endpoints[0]);
        Assert.DoesNotContain("https://open-api.hupun.com/api/erp/b2c/trades/open/erp/opentrade/list/trades", endpoints);
    }

    [Fact]
    public void BuildEndpointCandidates_QueryMode_ShouldResolveFromConfiguredExactGoodsEndpoint()
    {
        var endpoints = HupunB2cTradeUploader.BuildEndpointCandidatesForTesting(
            "https://open-api.hupun.com/api/erp/goods/spec/open/query/goodswithspeclist",
            "/erp/goods/spec/open/query/goodswithspeclist",
            "open-api.hupun.com");

        Assert.NotEmpty(endpoints);
        Assert.Equal("https://open-api.hupun.com/api/erp/goods/spec/open/query/goodswithspeclist", endpoints[0]);
        Assert.DoesNotContain(
            "https://open-api.hupun.com/api/erp/goods/spec/open/query/goodswithspeclist/erp/goods/spec/open/query/goodswithspeclist",
            endpoints);
    }

    [Fact]
    public void BuildUploadEndpointCandidates_ShouldPreferOfficialB2cTradeOpen()
    {
        var endpoints = HupunB2cTradeUploader.BuildUploadEndpointCandidatesForTesting("https://erp-open.hupun.com/api");

        Assert.NotEmpty(endpoints);
        Assert.Contains("https://open-api.hupun.com/api/erp/b2c/trades/open", endpoints);
        Assert.Equal("https://open-api.hupun.com/api/erp/b2c/trades/open", endpoints[0]);
        Assert.Contains("https://erp-open.hupun.com/api/erp/b2c/trades/open", endpoints);
    }

    [Fact]
    public void BuildUploadEndpointCandidates_ShouldAcceptConfiguredExactUploadEndpoint()
    {
        var endpoints = HupunB2cTradeUploader.BuildUploadEndpointCandidatesForTesting(
            "https://open-api.hupun.com/api/erp/b2c/trades/open");

        Assert.NotEmpty(endpoints);
        Assert.Equal("https://open-api.hupun.com/api/erp/b2c/trades/open", endpoints[0]);
        Assert.DoesNotContain("https://open-api.hupun.com/api/erp/b2c/trades/open/erp/b2c/trades/open", endpoints);
    }
}
