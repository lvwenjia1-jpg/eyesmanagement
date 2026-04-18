using OrderTextTrainer.Core.Models;
using WpfApp11;
using Xunit;

namespace pc.Tests;

public sealed class CatalogSkuResolverTests
{
    [Fact]
    public void RefreshDrafts_ShouldPreferTwoPieceDailyCandidates_WhenSourceOnlySaysDailyDisposable()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "游仙红", "350", "SKU-2P-游仙红-350"),
            CreateCatalogEntry("日抛10片", "游仙红", "350", "SKU-10P-游仙红-350")
        });

        var text = """
                   lenspop日抛
                   游仙红350五副
                   收件人: 无语子lenspop
                   手机号码: 15600580108
                   所在地区: 北京北京市朝阳区麦子店街道
                   详细地址: 嘉和丽园公寓A栋1202
                   """;

        var drafts = factory.CreateDrafts(text, snapshot, null, out _);
        var draft = Assert.Single(drafts);
        var item = Assert.Single(draft.Items);
        Assert.Equal("日抛2片", item.WearPeriod);

        resolver.RefreshDrafts(drafts, snapshot);

        Assert.Equal("SKU-2P-游仙红-350", item.ProductCode);
        Assert.NotEmpty(item.ProductCodeOptions);
        Assert.Equal("SKU-2P-游仙红-350", item.ProductCodeOptions[0].ProductCode);
    }

    [Fact]
    public void RefreshDrafts_ShouldPreferTenPieceDailyCandidates_WhenSourceExplicitlySaysTenPiece()
    {
        var factory = new OrderDraftFactory();
        var resolver = new CatalogSkuResolver();
        var snapshot = BuildSnapshot(new[]
        {
            CreateCatalogEntry("日抛2片", "游仙红", "350", "SKU-2P-游仙红-350"),
            CreateCatalogEntry("日抛10片", "游仙红", "350", "SKU-10P-游仙红-350")
        });

        var text = """
                   lenspop日抛十片装
                   游仙红350五副
                   收件人: 无语子lenspop
                   手机号码: 15600580108
                   所在地区: 北京北京市朝阳区麦子店街道
                   详细地址: 嘉和丽园公寓A栋1202
                   """;

        var drafts = factory.CreateDrafts(text, snapshot, null, out _);
        var draft = Assert.Single(drafts);
        var item = Assert.Single(draft.Items);
        Assert.Equal("日抛10片", item.WearPeriod);

        resolver.RefreshDrafts(drafts, snapshot);

        Assert.Equal("SKU-10P-游仙红-350", item.ProductCode);
        Assert.NotEmpty(item.ProductCodeOptions);
        Assert.Equal("SKU-10P-游仙红-350", item.ProductCodeOptions[0].ProductCode);
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
                new() { Alias = "日抛两片", WearPeriod = "日抛2片" },
                new() { Alias = "日抛十片", WearPeriod = "日抛10片" },
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
