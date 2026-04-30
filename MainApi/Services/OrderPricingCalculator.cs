using MainApi.Domain;

namespace MainApi.Services;

public static class OrderPricingCalculator
{
    public static IReadOnlyList<OrderPricingLineResult> Calculate(
        IReadOnlyList<OrderPricingInputItem> items,
        IReadOnlyList<PriceRuleRecord> rules)
    {
        var baseRules = rules
            .Where(rule => rule.IsActive && rule.RuleType == PriceRuleTypes.Base && !string.IsNullOrWhiteSpace(rule.SpecificationToken))
            .GroupBy(rule => rule.SpecificationToken.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var bulkRules = rules
            .Where(rule => rule.IsActive && rule.RuleType == PriceRuleTypes.Bulk && !string.IsNullOrWhiteSpace(rule.SpecificationToken) && rule.RequiredQuantity > 1)
            .GroupBy(rule => rule.SpecificationToken.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(rule => rule.RequiredQuantity).ThenBy(rule => rule.PriceValue).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var clearanceRules = rules
            .Where(rule => rule.IsActive && rule.RuleType == PriceRuleTypes.Clearance && !string.IsNullOrWhiteSpace(rule.SpecificationToken) && !string.IsNullOrWhiteSpace(rule.ModelToken))
            .GroupBy(rule => BuildClearanceKey(rule.SpecificationToken, rule.ModelToken), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var clearanceThresholds = rules
            .Where(rule => rule.IsActive && rule.RuleType == PriceRuleTypes.ClearanceThreshold && !string.IsNullOrWhiteSpace(rule.SpecificationToken) && rule.RequiredQuantity > 0)
            .GroupBy(rule => rule.SpecificationToken.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(rule => rule.RequiredQuantity).First(), StringComparer.OrdinalIgnoreCase);

        var units = ExpandUnits(items, clearanceRules);
        foreach (var specificationGroup in units.GroupBy(unit => unit.SpecificationToken, StringComparer.OrdinalIgnoreCase))
        {
            var specificationToken = specificationGroup.Key;
            var groupUnits = specificationGroup.ToList();
            ApplyClearancePricing(groupUnits, clearanceThresholds.GetValueOrDefault(specificationToken));
            ApplyRegularPricing(
                groupUnits.Where(unit => !unit.HasAssignedPrice).ToList(),
                baseRules.GetValueOrDefault(specificationToken),
                bulkRules.GetValueOrDefault(specificationToken));
        }

        return Aggregate(items, units);
    }

    private static List<PricingUnit> ExpandUnits(
        IReadOnlyList<OrderPricingInputItem> items,
        IReadOnlyDictionary<string, PriceRuleRecord> clearanceRules)
    {
        var result = new List<PricingUnit>();
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            var item = items[itemIndex];
            var quantity = Math.Max(0, item.Quantity);
            var isClearanceEligible = clearanceRules.ContainsKey(BuildClearanceKey(item.SpecificationToken, item.ModelToken));
            for (var quantityIndex = 0; quantityIndex < quantity; quantityIndex++)
            {
                result.Add(new PricingUnit(itemIndex, item.SpecificationToken, isClearanceEligible));
            }
        }

        return result;
    }

    private static void ApplyClearancePricing(List<PricingUnit> units, PriceRuleRecord? thresholdRule)
    {
        if (thresholdRule is null || thresholdRule.RequiredQuantity <= 0)
        {
            return;
        }

        var eligibleUnits = units
            .Where(unit => unit.IsClearanceEligible)
            .ToList();

        var clearanceCount = eligibleUnits.Count / thresholdRule.RequiredQuantity * thresholdRule.RequiredQuantity;
        var packageCount = clearanceCount / thresholdRule.RequiredQuantity;
        for (var packageIndex = 0; packageIndex < packageCount; packageIndex++)
        {
            var amounts = DistributeAmount(thresholdRule.PriceValue, thresholdRule.RequiredQuantity);
            for (var offset = 0; offset < thresholdRule.RequiredQuantity; offset++)
            {
                var unit = eligibleUnits[packageIndex * thresholdRule.RequiredQuantity + offset];
                unit.Assign(
                    amounts[offset],
                    thresholdRule.Id,
                    thresholdRule.PriceName,
                    thresholdRule.PriceName);
            }
        }
    }

    private static void ApplyRegularPricing(
        List<PricingUnit> units,
        PriceRuleRecord? baseRule,
        IReadOnlyList<PriceRuleRecord>? bulkRules)
    {
        if (units.Count == 0)
        {
            return;
        }

        var basePrice = baseRule?.PriceValue ?? 0;
        var dp = new int[units.Count + 1];
        var choices = new PricingChoice[units.Count + 1];
        for (var count = 1; count <= units.Count; count++)
        {
            dp[count] = checked(dp[count - 1] + basePrice);
            choices[count] = new PricingChoice(baseRule?.Id, baseRule?.PriceName ?? string.Empty, baseRule?.PriceName ?? string.Empty, 1, basePrice);

            if (bulkRules is null)
            {
                continue;
            }

            foreach (var bulkRule in bulkRules)
            {
                if (bulkRule.RequiredQuantity <= 0 || count < bulkRule.RequiredQuantity)
                {
                    continue;
                }

                var candidate = checked(dp[count - bulkRule.RequiredQuantity] + bulkRule.PriceValue);
                if (candidate >= dp[count])
                {
                    continue;
                }

                dp[count] = candidate;
                choices[count] = new PricingChoice(
                    bulkRule.Id,
                    bulkRule.PriceName,
                    bulkRule.PriceName,
                    bulkRule.RequiredQuantity,
                    bulkRule.PriceValue);
            }
        }

        var cursor = units.Count;
        var unitIndex = 0;
        while (cursor > 0)
        {
            var choice = choices[cursor];
            var bundleSize = Math.Max(1, choice.Quantity);
            var amounts = DistributeAmount(choice.TotalAmount, bundleSize);
            for (var offset = 0; offset < bundleSize && unitIndex < units.Count; offset++, unitIndex++)
            {
                units[unitIndex].Assign(amounts[offset], choice.RuleId, choice.PriceName, choice.ComponentLabel);
            }

            cursor -= bundleSize;
        }
    }

    private static IReadOnlyList<OrderPricingLineResult> Aggregate(
        IReadOnlyList<OrderPricingInputItem> items,
        IReadOnlyList<PricingUnit> units)
    {
        var unitLookup = units
            .GroupBy(unit => unit.ItemIndex)
            .ToDictionary(group => group.Key, group => group.ToList());

        var results = new List<OrderPricingLineResult>(items.Count);
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            var item = items[itemIndex];
            unitLookup.TryGetValue(itemIndex, out var itemUnits);
            itemUnits ??= new List<PricingUnit>();

            var lineAmount = itemUnits.Sum(unit => unit.AssignedAmount);
            var firstRuleId = itemUnits.Select(unit => unit.RuleId).Distinct().Take(2).ToArray();
            var firstPriceName = itemUnits.Select(unit => unit.PriceName).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            var summary = BuildSummary(itemUnits);

            results.Add(new OrderPricingLineResult
            {
                ItemIndex = itemIndex,
                PriceRuleId = firstRuleId.Length == 1 ? firstRuleId[0] : null,
                PriceName = firstPriceName.Length == 1
                    ? firstPriceName[0] ?? string.Empty
                    : summary,
                UnitPrice = item.Quantity > 0 ? lineAmount / item.Quantity : 0,
                LineAmount = lineAmount
            });
        }

        return results;
    }

    private static string BuildSummary(IReadOnlyList<PricingUnit> units)
    {
        if (units.Count == 0)
        {
            return string.Empty;
        }

        var parts = units
            .GroupBy(unit => unit.ComponentLabel, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}x{group.Count()}")
            .ToArray();

        var summary = string.Join(" + ", parts);
        return summary.Length <= 128 ? summary : summary[..128];
    }

    private static int[] DistributeAmount(int totalAmount, int quantity)
    {
        var result = new int[quantity];
        if (quantity <= 0)
        {
            return result;
        }

        var average = totalAmount / quantity;
        var remainder = totalAmount % quantity;
        for (var index = 0; index < quantity; index++)
        {
            result[index] = average + (index < remainder ? 1 : 0);
        }

        return result;
    }

    private static string BuildClearanceKey(string? specificationToken, string? modelToken)
    {
        return $"{Normalize(specificationToken)}||{Normalize(modelToken)}";
    }

    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    public sealed class OrderPricingInputItem
    {
        public string SpecificationToken { get; set; } = string.Empty;

        public string ModelToken { get; set; } = string.Empty;

        public int Quantity { get; set; }
    }

    public sealed class OrderPricingLineResult
    {
        public int ItemIndex { get; set; }

        public long? PriceRuleId { get; set; }

        public string PriceName { get; set; } = string.Empty;

        public int UnitPrice { get; set; }

        public int LineAmount { get; set; }
    }

    private sealed class PricingUnit
    {
        public PricingUnit(int itemIndex, string specificationToken, bool isClearanceEligible)
        {
            ItemIndex = itemIndex;
            SpecificationToken = specificationToken;
            IsClearanceEligible = isClearanceEligible;
        }

        public int ItemIndex { get; }

        public string SpecificationToken { get; }

        public bool IsClearanceEligible { get; }

        public bool HasAssignedPrice { get; private set; }

        public int AssignedAmount { get; private set; }

        public long? RuleId { get; private set; }

        public string PriceName { get; private set; } = string.Empty;

        public string ComponentLabel { get; private set; } = string.Empty;

        public void Assign(int amount, long? ruleId, string priceName, string componentLabel)
        {
            HasAssignedPrice = true;
            AssignedAmount = amount;
            RuleId = ruleId;
            PriceName = priceName;
            ComponentLabel = componentLabel;
        }
    }

    private sealed record PricingChoice(long? RuleId, string PriceName, string ComponentLabel, int Quantity, int TotalAmount);
}
