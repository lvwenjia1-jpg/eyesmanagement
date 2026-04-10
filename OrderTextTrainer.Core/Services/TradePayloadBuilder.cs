using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OrderTextTrainer.Core.Models;

namespace OrderTextTrainer.Core.Services;

public sealed class TradePayloadBuilder
{
    public IReadOnlyList<TradeOpenPayload> Build(ParseResult result)
    {
        return result.Orders.Select((order, index) => Build(order, index)).ToList();
    }

    public TradeOpenPayload Build(ParsedOrder order, int orderIndex = 0)
    {
        var addressParts = SplitAddress(order.Address);
        var tradeId = BuildTradeId(order, orderIndex);
        var payload = new TradeOpenPayload
        {
            TradeId = tradeId,
            OrderId = tradeId,
            BuyerNick = order.CustomerName ?? string.Empty,
            ReceiverName = order.CustomerName ?? string.Empty,
            ReceiverMobile = order.Phone ?? string.Empty,
            ReceiverState = addressParts.State,
            ReceiverCity = addressParts.City,
            ReceiverDistrict = addressParts.District,
            ReceiverAddress = addressParts.Detail,
            SellerMemo = order.Remark
        };

        foreach (var item in order.Items)
        {
            payload.TradeDetails.Add(new TradeOpenPayloadItem
            {
                GoodsCode = BuildGoodsCode(order, item),
                GoodsName = BuildGoodsName(order, item),
                Quantity = item.Quantity ?? 1,
                Power = item.PowerSummary,
                IsTrial = item.IsTrial
            });
        }

        return payload;
    }

    private static string BuildTradeId(ParsedOrder order, int orderIndex)
    {
        var raw = $"{orderIndex + 1}|{order.CustomerName}|{order.Phone}|{order.Address}|{order.SourceText}";
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        var suffix = Convert.ToHexString(bytes)[..12];
        return $"WPF{suffix}";
    }

    private static string BuildGoodsCode(ParsedOrder order, OrderItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ResolvedGoodsCode))
        {
            return item.ResolvedGoodsCode!;
        }

        if (string.Equals(item.MatchSource, "legacy", StringComparison.OrdinalIgnoreCase))
        {
            return LegacyGoodsValueBuilder.BuildGoodsCode(order);
        }

        return "未匹配";
    }

    private static string BuildGoodsName(ParsedOrder order, OrderItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ResolvedGoodsName))
        {
            return item.ResolvedGoodsName!;
        }

        if (string.Equals(item.MatchSource, "legacy", StringComparison.OrdinalIgnoreCase))
        {
            return LegacyGoodsValueBuilder.BuildGoodsName(order, item);
        }

        return item.RawText;
    }

    private static AddressParts SplitAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return AddressParts.Empty;
        }

        var cleaned = Regex.Replace(address, @"\s+", " ").Trim();

        var tokenParts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokenParts.Length >= 4 &&
            tokenParts[0].EndsWith("省", StringComparison.OrdinalIgnoreCase) &&
            tokenParts[1].EndsWith("市", StringComparison.OrdinalIgnoreCase) &&
            (tokenParts[2].EndsWith("区", StringComparison.OrdinalIgnoreCase) || tokenParts[2].EndsWith("县", StringComparison.OrdinalIgnoreCase) || tokenParts[2].EndsWith("市", StringComparison.OrdinalIgnoreCase)))
        {
            return new AddressParts(
                tokenParts[0],
                tokenParts[1],
                tokenParts[2],
                string.Join(' ', tokenParts.Skip(3)));
        }

        var match = Regex.Match(
            cleaned,
            @"^(?<state>.*?(?:省|自治区|特别行政区|市))?\s*(?<city>.*?(?:市|州|盟|地区))?\s*(?<district>.*?(?:区|县|旗))?\s*(?<detail>.*)$");

        if (!match.Success)
        {
            return new AddressParts(string.Empty, string.Empty, string.Empty, cleaned);
        }

        var state = match.Groups["state"].Value.Trim();
        var city = match.Groups["city"].Value.Trim();
        var district = match.Groups["district"].Value.Trim();
        var detail = match.Groups["detail"].Value.Trim();

        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = cleaned;
        }

        return new AddressParts(state, city, district, detail);
    }

    private readonly record struct AddressParts(string State, string City, string District, string Detail)
    {
        public static AddressParts Empty => new(string.Empty, string.Empty, string.Empty, string.Empty);
    }
}
