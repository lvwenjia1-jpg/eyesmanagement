using OrderTextTrainer.Core.Models;

namespace OrderTextTrainer.Core.Services;

public static class LegacyGoodsValueBuilder
{
    public static string BuildGoodsCode(ParsedOrder order)
    {
        var pieces = new[] { order.Brand, order.WearPeriod }
            .Where(value => !string.IsNullOrWhiteSpace(value));

        var goodsCode = string.Concat(pieces);
        return string.IsNullOrWhiteSpace(goodsCode) ? "未分类商品" : goodsCode;
    }

    public static string BuildGoodsName(ParsedOrder order, OrderItem item)
    {
        var pieces = new[]
        {
            order.WearPeriod,
            item.ProductName,
            item.PowerSummary
        };

        return string.Join(' ', pieces.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
