using MainApi.Domain;

namespace MainApi.Services;

public static class OrderPricingCalculator
{
    private static readonly char[] ModelTokenSeparators = new[] { ',', '\uFF0C', ';', '\uFF1B', '\u3001', '|', '\r', '\n' };
    private static readonly char[] SpecificationTokenSeparators = new[] { ',', '\uFF0C', ';', '\uFF1B', '\u3001', '|', '\r', '\n' };

    public static IReadOnlyList<OrderPricingLineResult> Calculate(
        IReadOnlyList<OrderPricingInputItem> items,
        IReadOnlyList<PriceRuleRecord> rules)
    {
        var baseRules = rules
            .Where(rule => rule.IsActive && rule.RuleType == PriceRuleTypes.Base && !string.IsNullOrWhiteSpace(rule.SpecificationToken))
            .GroupBy(rule => rule.SpecificationToken.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var singlePieceRules = BuildSinglePieceRuleLookup(rules);

        var bulkRules = rules
            .Where(rule => rule.IsActive && rule.RuleType == PriceRuleTypes.Bulk && !string.IsNullOrWhiteSpace(rule.SpecificationToken) && rule.RequiredQuantity > 1)
            .GroupBy(rule => rule.SpecificationToken.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(rule => rule.RequiredQuantity).ThenBy(rule => rule.PriceValue).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var clearanceRules = BuildClearanceRuleLookup(rules);
        // A configured single-piece rule opts a half-year/yearly period into exact piece pricing.
        // Periods without one retain the historical behavior of rounding an odd count up to a pair.
        var units = ExpandUnits(items, singlePieceRules);

        ApplyClearancePricing(units, clearanceRules);

        foreach (var specificationGroup in units.GroupBy(unit => unit.SpecificationToken, StringComparer.OrdinalIgnoreCase))
        {
            var specificationToken = specificationGroup.Key;
            var groupUnits = specificationGroup.ToList();
            var unitMultiplier = GetQuantityUnitMultiplier(specificationToken);

            ApplyRegularPricing(
                groupUnits.Where(unit => !unit.IsSinglePiece && !unit.HasAssignedPrice).ToList(),
                baseRules.GetValueOrDefault(specificationToken),
                bulkRules.GetValueOrDefault(specificationToken),
                unitMultiplier);

            ApplySinglePiecePricing(groupUnits.Where(unit => unit.IsSinglePiece && !unit.HasAssignedPrice).ToList(), singlePieceRules);
        }

        return Aggregate(items, units);
    }

    private static List<ClearanceRuleEntry> BuildClearanceRuleLookup(IReadOnlyList<PriceRuleRecord> rules)
    {
        var entries = new List<ClearanceRuleEntry>();
        foreach (var rule in rules.Where(rule =>
                     rule.IsActive &&
                     rule.RuleType == PriceRuleTypes.Clearance &&
                     !string.IsNullOrWhiteSpace(rule.SpecificationToken) &&
                     !string.IsNullOrWhiteSpace(rule.ModelToken) &&
                     rule.RequiredQuantity > 0))
        {
            if (ClearanceRuleSelectionSerializer.TryDeserialize(rule.ClearanceSelectionsJson, out var pairedSelections) ||
                ClearanceRuleSelectionSerializer.TryDeserialize(rule.ModelToken, out pairedSelections))
            {
                if (pairedSelections.Count == 0)
                {
                    continue;
                }

                entries.Add(new ClearanceRuleEntry(rule, pairedSelections));
                continue;
            }

            var specificationTokens = SplitSpecificationTokens(rule.SpecificationToken);
            var modelTokens = SplitModelTokens(rule.ModelToken);
            if (specificationTokens.Count == 0 || modelTokens.Count == 0)
            {
                continue;
            }

            var legacySelections = specificationTokens
                .SelectMany(specification => modelTokens.Select(model => new ClearanceRuleSelection
                {
                    SpecificationToken = specification,
                    ModelToken = model
                }))
                .ToList();
            entries.Add(new ClearanceRuleEntry(rule, legacySelections));
        }

        entries.Sort(static (left, right) =>
        {
            var requiredCompare = right.Rule.RequiredQuantity.CompareTo(left.Rule.RequiredQuantity);
            if (requiredCompare != 0)
            {
                return requiredCompare;
            }

            var priceCompare = left.Rule.PriceValue.CompareTo(right.Rule.PriceValue);
            if (priceCompare != 0)
            {
                return priceCompare;
            }

            return left.Rule.Id.CompareTo(right.Rule.Id);
        });

        return entries;
    }

    private static SinglePieceRuleLookup BuildSinglePieceRuleLookup(IReadOnlyList<PriceRuleRecord> rules)
    {
        var rulesBySelection = new Dictionary<string, PriceRuleRecord>(StringComparer.OrdinalIgnoreCase);
        var fallbackRulesBySpecification = new Dictionary<string, PriceRuleRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules.Where(rule =>
                     rule.IsActive &&
                     rule.RuleType == PriceRuleTypes.SinglePiece &&
                     !string.IsNullOrWhiteSpace(rule.SpecificationToken)))
        {
            var specificationToken = Normalize(rule.SpecificationToken);
            var modelTokens = SplitModelTokens(rule.ModelToken);
            if (modelTokens.Count == 0)
            {
                fallbackRulesBySpecification.TryAdd(specificationToken, rule);
                continue;
            }

            foreach (var modelToken in modelTokens)
            {
                rulesBySelection.TryAdd(BuildClearanceKey(specificationToken, modelToken), rule);
            }
        }

        return new SinglePieceRuleLookup(rulesBySelection, fallbackRulesBySpecification);
    }

    private static List<PricingUnit> ExpandUnits(
        IReadOnlyList<OrderPricingInputItem> items,
        SinglePieceRuleLookup singlePieceRules)
    {
        var result = new List<PricingUnit>();
        var pricingQuantities = BuildPricingQuantities(items, singlePieceRules);
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            var item = items[itemIndex];
            var quantity = pricingQuantities[itemIndex];
            for (var quantityIndex = 0; quantityIndex < quantity.PairQuantity; quantityIndex++)
            {
                result.Add(new PricingUnit(itemIndex, item.SpecificationToken, Normalize(item.ModelToken), isSinglePiece: false));
            }

            for (var quantityIndex = 0; quantityIndex < quantity.SinglePieceQuantity; quantityIndex++)
            {
                result.Add(new PricingUnit(itemIndex, item.SpecificationToken, Normalize(item.ModelToken), isSinglePiece: true));
            }
        }

        return result;
    }

    private static void ApplyClearancePricing(IReadOnlyList<PricingUnit> units, IReadOnlyList<ClearanceRuleEntry> rules)
    {
        if (units.Count == 0 || rules.Count == 0)
        {
            return;
        }

        foreach (var rule in rules)
        {
            var effectiveRequiredQuantity = GetEffectiveRequiredQuantity(rule.Rule, 1);
            if (effectiveRequiredQuantity <= 0)
            {
                continue;
            }

            while (true)
            {
                var remainingUnits = units
                    .Where(unit =>
                        // Clearance quantities are configured in pairs, so a single-piece remainder must not consume a pair quota.
                        !unit.IsSinglePiece &&
                        !unit.HasAssignedPrice &&
                        rule.SelectionKeys.Contains(BuildClearanceKey(unit.SpecificationToken, unit.ModelToken)))
                    .ToList();
                if (remainingUnits.Count < effectiveRequiredQuantity)
                {
                    break;
                }

                var amounts = DistributeAmount(rule.Rule.PriceValue, effectiveRequiredQuantity);
                for (var offset = 0; offset < effectiveRequiredQuantity; offset++)
                {
                    var unit = remainingUnits[offset];
                    unit.Assign(
                        amounts[offset],
                        rule.Rule.Id,
                        rule.Rule.PriceName,
                        rule.Rule.PriceName);
                }
            }
        }
    }

    private static void ApplyRegularPricing(
        List<PricingUnit> units,
        PriceRuleRecord? baseRule,
        IReadOnlyList<PriceRuleRecord>? bulkRules,
        int unitMultiplier)
    {
        if (units.Count == 0)
        {
            return;
        }

        var remainingUnits = units.Where(unit => !unit.HasAssignedPrice).ToList();
        if (bulkRules is not null)
        {
            foreach (var bulkRule in bulkRules)
            {
                var effectiveRequiredQuantity = GetEffectiveRequiredQuantity(bulkRule, unitMultiplier);
                if (effectiveRequiredQuantity <= 0)
                {
                    continue;
                }

                while (remainingUnits.Count >= effectiveRequiredQuantity)
                {
                    var amounts = DistributeAmount(bulkRule.PriceValue, effectiveRequiredQuantity);
                    for (var offset = 0; offset < effectiveRequiredQuantity; offset++)
                    {
                        var unit = remainingUnits[offset];
                        unit.Assign(amounts[offset], bulkRule.Id, bulkRule.PriceName, bulkRule.PriceName);
                    }

                    remainingUnits.RemoveRange(0, effectiveRequiredQuantity);
                }
            }
        }

        var basePrice = baseRule?.PriceValue ?? 0;
        foreach (var unit in remainingUnits)
        {
            unit.Assign(basePrice, baseRule?.Id, baseRule?.PriceName ?? string.Empty, baseRule?.PriceName ?? string.Empty);
        }
    }

    private static void ApplySinglePiecePricing(List<PricingUnit> units, SinglePieceRuleLookup singlePieceRules)
    {
        if (units.Count == 0)
        {
            return;
        }

        foreach (var unit in units)
        {
            var singlePieceRule = singlePieceRules.Find(unit.SpecificationToken, unit.ModelToken);
            unit.Assign(
                singlePieceRule?.PriceValue ?? 0,
                singlePieceRule?.Id,
                singlePieceRule?.PriceName ?? string.Empty,
                BuildSinglePieceComponentLabel(singlePieceRule, unit));
        }
    }

    private static string BuildSinglePieceComponentLabel(PriceRuleRecord? rule, PricingUnit unit)
    {
        if (rule is null)
        {
            return string.Empty;
        }

        // A rule can cover many models, but an order line should identify only the model that consumed the price.
        return $"单片 / {Normalize(unit.SpecificationToken)} / {Normalize(unit.ModelToken)}";
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
            var firstPriceName = itemUnits.Select(unit => unit.ComponentLabel).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            var summary = BuildSummary(itemUnits);

            results.Add(new OrderPricingLineResult
            {
                ItemIndex = itemIndex,
                PriceRuleId = firstRuleId.Length == 1 ? firstRuleId[0] : null,
                PriceName = firstPriceName.Length == 1
                    ? firstPriceName[0] ?? string.Empty
                    : summary,
                UnitPrice = item.Quantity > 0 ? lineAmount / item.Quantity : 0,
                LineAmount = lineAmount,
                Components = itemUnits
                    .Where(unit => !string.IsNullOrWhiteSpace(unit.PriceName))
                    .GroupBy(unit => new { unit.RuleId, unit.PriceName, unit.ComponentLabel })
                    .Select(group => new OrderPricingComponent
                    {
                        PriceRuleId = group.Key.RuleId,
                        PriceName = group.Key.PriceName,
                        DisplayName = group.Key.ComponentLabel,
                        Quantity = group.Count(),
                        Amount = group.Sum(unit => unit.AssignedAmount)
                    })
                    .ToList()
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

    private static List<string> SplitModelTokens(string? value)
    {
        return (value ?? string.Empty)
            .Split(ModelTokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(modelToken => !string.IsNullOrWhiteSpace(modelToken))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> SplitSpecificationTokens(string? value)
    {
        return (value ?? string.Empty)
            .Split(SpecificationTokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(specificationToken => !string.IsNullOrWhiteSpace(specificationToken))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static PricingQuantity[] BuildPricingQuantities(
        IReadOnlyList<OrderPricingInputItem> items,
        SinglePieceRuleLookup singlePieceRules)
    {
        var result = Enumerable.Range(0, items.Count).Select(_ => new PricingQuantity()).ToArray();
        var groupedIndices = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var safeQuantity = Math.Max(0, item.Quantity);
            if (safeQuantity == 0)
            {
                continue;
            }

            if (!ShouldHalveQuantityForPricing(item.WearPeriodToken))
            {
                result[index].PairQuantity = safeQuantity;
                continue;
            }

            var usesSinglePiecePricing = singlePieceRules.Find(item.SpecificationToken, item.ModelToken) is not null;
            var groupKey = $"{BuildClearanceKey(item.SpecificationToken, item.ModelToken)}||{usesSinglePiecePricing}";
            if (!groupedIndices.TryGetValue(groupKey, out var list))
            {
                list = new List<int>();
                groupedIndices[groupKey] = list;
            }

            list.Add(index);
        }

        foreach (var pair in groupedIndices)
        {
            _ = pair.Key;
            var indices = pair.Value;
            var carry = 0;
            var totalRawQuantity = 0;
            var lastPositiveIndex = -1;
            var usesSinglePiecePricing = singlePieceRules.Find(items[indices[0]].SpecificationToken, items[indices[0]].ModelToken) is not null;

            foreach (var index in indices)
            {
                var rawQuantity = Math.Max(0, items[index].Quantity);
                totalRawQuantity += rawQuantity;
                if (rawQuantity > 0)
                {
                    lastPositiveIndex = index;
                }

                var combined = rawQuantity + carry;
                result[index].PairQuantity = combined / 2;
                carry = combined % 2;
            }

            if (totalRawQuantity > 0 && (totalRawQuantity % 2) != 0 && lastPositiveIndex >= 0)
            {
                if (usesSinglePiecePricing)
                {
                    // The odd remainder is independently priced instead of being promoted to a full pair.
                    result[lastPositiveIndex].SinglePieceQuantity = 1;
                }
                else
                {
                    result[lastPositiveIndex].PairQuantity += 1;
                }
            }
        }

        return result;
    }

    private static bool ShouldHalveQuantityForPricing(string? wearPeriodToken)
    {
        var normalized = Normalize(wearPeriodToken);
        var compact = normalized.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(compact))
        {
            return false;
        }

        return compact.Contains("\u534A\u5E74\u629B", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("\u534A\u629B", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("\u5E74\u629B", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("\u4E00\u5E74\u629B", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("halfyear", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("half-year", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("semiannual", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("yearly", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("1year", StringComparison.OrdinalIgnoreCase) ||
               compact.Contains("oneyear", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetEffectiveRequiredQuantity(PriceRuleRecord rule, int unitMultiplier)
    {
        if (rule.RequiredQuantity <= 0)
        {
            return 0;
        }

        return checked(rule.RequiredQuantity * Math.Max(1, unitMultiplier));
    }

    private static int GetQuantityUnitMultiplier(string? specificationToken)
    {
        _ = specificationToken;
        // RequiredQuantity is maintained by price rules and should be applied directly.
        return 1;
    }

    public sealed class OrderPricingInputItem
    {
        public string SpecificationToken { get; set; } = string.Empty;

        public string WearPeriodToken { get; set; } = string.Empty;

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

        public IReadOnlyList<OrderPricingComponent> Components { get; set; } = Array.Empty<OrderPricingComponent>();
    }

    public sealed class OrderPricingComponent
    {
        public long? PriceRuleId { get; set; }

        public string PriceName { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public int Amount { get; set; }
    }

    private sealed class PricingUnit
    {
        public PricingUnit(int itemIndex, string specificationToken, string modelToken, bool isSinglePiece)
        {
            ItemIndex = itemIndex;
            SpecificationToken = specificationToken;
            ModelToken = modelToken;
            IsSinglePiece = isSinglePiece;
        }

        public int ItemIndex { get; }

        public string SpecificationToken { get; }

        public string ModelToken { get; }

        public bool IsSinglePiece { get; }

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

    private sealed class PricingQuantity
    {
        public int PairQuantity { get; set; }

        public int SinglePieceQuantity { get; set; }
    }

    private sealed class SinglePieceRuleLookup
    {
        private readonly IReadOnlyDictionary<string, PriceRuleRecord> _rulesBySelection;
        private readonly IReadOnlyDictionary<string, PriceRuleRecord> _fallbackRulesBySpecification;

        public SinglePieceRuleLookup(
            IReadOnlyDictionary<string, PriceRuleRecord> rulesBySelection,
            IReadOnlyDictionary<string, PriceRuleRecord> fallbackRulesBySpecification)
        {
            _rulesBySelection = rulesBySelection;
            _fallbackRulesBySpecification = fallbackRulesBySpecification;
        }

        public PriceRuleRecord? Find(string? specificationToken, string? modelToken)
        {
            var selectionKey = BuildClearanceKey(specificationToken, modelToken);
            if (_rulesBySelection.TryGetValue(selectionKey, out var rule))
            {
                return rule;
            }

            return _fallbackRulesBySpecification.GetValueOrDefault(Normalize(specificationToken));
        }
    }

    private sealed class ClearanceRuleEntry
    {
        public ClearanceRuleEntry(PriceRuleRecord rule, IReadOnlyCollection<ClearanceRuleSelection> selections)
        {
            Rule = rule;
            SelectionKeys = new HashSet<string>(
                selections.Select(selection => BuildClearanceKey(selection.SpecificationToken, selection.ModelToken)),
                StringComparer.OrdinalIgnoreCase);
        }

        public PriceRuleRecord Rule { get; }

        public HashSet<string> SelectionKeys { get; }
    }

}
