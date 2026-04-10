using OrderTextTrainer.Core.Models;

namespace OrderTextTrainer.Core.Services;

public sealed class WearPeriodResolver
{
    private static readonly string[] PreferredPeriods =
    {
        "年抛",
        "半年抛",
        "日抛10片装",
        "日抛2片装",
        "试戴片"
    };

    public IReadOnlyList<string> GetCandidates(ParserRuleSet ruleSet)
    {
        return PreferredPeriods
            .Concat(ruleSet.WearTypeAliases.Keys)
            .Where(value => !string.Equals(value, "日抛", StringComparison.OrdinalIgnoreCase))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Resolve(ParseResult result, ParserRuleSet ruleSet, IReadOnlyList<WearPeriodOverride> overrides)
    {
        var candidates = GetCandidates(ruleSet);
        var overrideMap = overrides
            .Where(item => !string.IsNullOrWhiteSpace(item.OrderKey))
            .GroupBy(item => item.OrderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var order in result.Orders)
        {
            var orderKey = GetOrderKey(order);
            order.DetectedWearPeriod ??= order.WearPeriod;

            if (overrideMap.TryGetValue(orderKey, out var overrideItem) &&
                candidates.Contains(overrideItem.WearPeriod, StringComparer.OrdinalIgnoreCase))
            {
                order.WearPeriod = overrideItem.WearPeriod;
                order.WearPeriodMatchSource = "manual";
                order.WearPeriodMatchNote = "已应用人工选择的商品周期。";
                continue;
            }

            if (!string.IsNullOrWhiteSpace(order.DetectedWearPeriod) &&
                candidates.Contains(order.DetectedWearPeriod, StringComparer.OrdinalIgnoreCase))
            {
                order.WearPeriod = order.DetectedWearPeriod;
                order.WearPeriodMatchSource = "exact";
                order.WearPeriodMatchNote = "已自动匹配商品周期。";
                continue;
            }

            order.WearPeriod = null;
            order.WearPeriodMatchSource = "pending";
            order.WearPeriodMatchNote = string.IsNullOrWhiteSpace(order.DetectedWearPeriod)
                ? "未识别出商品周期，请人工选择。"
                : $"识别到周期“{order.DetectedWearPeriod}”，但不在当前周期池中，请人工选择。";
        }
    }

    public string GetOrderKey(ParsedOrder order)
    {
        return MatchTextHelper.Compact(
            string.Join('|',
                new[]
                {
                    order.Brand,
                    order.CustomerName,
                    order.Phone,
                    order.Address,
                    order.SourceText
                }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }
}
