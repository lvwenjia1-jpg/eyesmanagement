using System.Text.RegularExpressions;
using OrderTextTrainer.Core.Models;

namespace OrderTextTrainer.Core.Services;

public sealed class ProductCatalogMatcher
{
    public void Resolve(ParseResult result, IReadOnlyList<ProductCatalogEntry> catalogEntries, IReadOnlyList<ProductMatchOverride> overrides)
    {
        var overrideMap = overrides
            .Where(item => !string.IsNullOrWhiteSpace(item.MatchKey))
            .GroupBy(item => item.MatchKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var catalogByCode = catalogEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ProductCode))
            .GroupBy(entry => entry.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var order in result.Orders)
        {
            foreach (var item in order.Items)
            {
                item.MatchCandidates.Clear();

                if (catalogEntries.Count == 0)
                {
                    item.MatchSource = "legacy";
                    item.ResolvedGoodsCode = LegacyGoodsValueBuilder.BuildGoodsCode(order);
                    item.ResolvedGoodsName = LegacyGoodsValueBuilder.BuildGoodsName(order, item);
                    item.MatchNote = "未导入商品表，使用旧规则。";
                    continue;
                }

                var matchKey = GetMatchKey(order, item);
                if (overrideMap.TryGetValue(matchKey, out var manualOverride) &&
                    catalogByCode.TryGetValue(manualOverride.ProductCode, out var manualCatalog))
                {
                    ApplyMatch(item, manualCatalog, "manual", "已应用人工选择。");
                    continue;
                }

                var exactMatches = FindExactMatches(order, item, catalogEntries).ToList();
                if (exactMatches.Count > 0)
                {
                    ApplyMatch(item, exactMatches[0], "exact", "已精确匹配商品表。");
                    if (exactMatches.Count > 1)
                    {
                        item.MatchCandidates = exactMatches;
                        item.MatchNote = "存在多个精确候选，已优先取第一项。";
                    }

                    continue;
                }

                item.MatchCandidates = FindCandidates(order, item, catalogEntries).Take(20).ToList();
                item.MatchSource = "pending";
                item.MatchNote = item.MatchCandidates.Count == 0
                    ? "未找到商品表匹配项，请从完整商品编码池中手工选择。"
                    : $"找到 {item.MatchCandidates.Count} 个候选，可继续从完整商品编码池中确认。";
            }
        }
    }

    public string GetMatchKey(ParsedOrder order, OrderItem item)
    {
        var keyParts = new[]
        {
            order.Brand,
            order.WearPeriod,
            item.ProductName,
            item.PowerSummary,
            item.RawText
        };

        return MatchTextHelper.Compact(string.Join('|', keyParts.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static void ApplyMatch(OrderItem item, ProductCatalogEntry catalogEntry, string source, string note)
    {
        item.MatchSource = source;
        item.ResolvedGoodsCode = catalogEntry.ProductCode;
        item.ResolvedGoodsName = string.IsNullOrWhiteSpace(catalogEntry.ProductName)
            ? catalogEntry.ProductCode
            : catalogEntry.ProductName;
        item.ResolvedSpecCode = catalogEntry.SpecCode;
        item.ResolvedBarcode = catalogEntry.Barcode;
        item.MatchNote = note;
        item.MatchCandidates.Clear();
    }

    private static IEnumerable<ProductCatalogEntry> FindExactMatches(ParsedOrder order, OrderItem item, IReadOnlyList<ProductCatalogEntry> catalogEntries)
    {
        var productTokens = new[]
        {
            order.Brand,
            order.WearPeriod,
            item.ProductName,
            item.RawText
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(MatchTextHelper.Compact)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToList();

        var degreeKey = ResolveDegreeKey(item);

        return catalogEntries
            .Where(entry =>
            {
                var entryBase = MatchTextHelper.Compact(GetCatalogDisplayName(entry));
                var entrySpec = MatchTextHelper.Compact(entry.SpecificationToken);
                var entryCode = MatchTextHelper.Compact(entry.ProductCode);
                var entryName = MatchTextHelper.Compact(entry.ProductName);

                var productMatched = productTokens.Any(token =>
                    token.Contains(entryBase, StringComparison.OrdinalIgnoreCase) ||
                    token.Contains(entrySpec, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(entryCode) && token.Contains(entryCode, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(entryName) && token.Contains(entryName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(entryBase) && entryBase.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(entryName) && entryName.EndsWith(token, StringComparison.OrdinalIgnoreCase)));

                if (!productMatched)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(entry.Degree) || string.IsNullOrWhiteSpace(degreeKey))
                {
                    return false;
                }

                return string.Equals(entry.Degree, degreeKey, StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(entry => MatchTextHelper.Compact(GetCatalogDisplayName(entry)).Length)
            .ThenByDescending(entry => MatchTextHelper.Compact(entry.ProductCode).Length);
    }

    private static IEnumerable<ProductCatalogEntry> FindCandidates(ParsedOrder order, OrderItem item, IReadOnlyList<ProductCatalogEntry> catalogEntries)
    {
        var productTokens = new[]
        {
            order.Brand,
            order.WearPeriod,
            item.ProductName,
            item.RawText
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(MatchTextHelper.Compact)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToList();

        var degreeKey = ResolveDegreeKey(item);

        return catalogEntries
            .Select(entry => new
            {
                Entry = entry,
                Score = ScoreCandidate(entry, productTokens, degreeKey)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => MatchTextHelper.Compact(item.Entry.BaseName).Length)
            .Select(item => item.Entry);
    }

    private static int ScoreCandidate(ProductCatalogEntry entry, IReadOnlyList<string> productTokens, string degreeKey)
    {
        var score = 0;
        var entryBase = MatchTextHelper.Compact(GetCatalogDisplayName(entry));
        var entrySpec = MatchTextHelper.Compact(entry.SpecificationToken);
        var entryCode = MatchTextHelper.Compact(entry.ProductCode);
        var entryName = MatchTextHelper.Compact(entry.ProductName);

        foreach (var token in productTokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entryBase) && token.Contains(entryBase, StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
            }

            if (!string.IsNullOrWhiteSpace(entrySpec) && token.Contains(entrySpec, StringComparison.OrdinalIgnoreCase))
            {
                score += 12;
            }

            if (!string.IsNullOrWhiteSpace(entryCode) && token.Contains(entryCode, StringComparison.OrdinalIgnoreCase))
            {
                score += 18;
            }

            if (!string.IsNullOrWhiteSpace(entryName) && token.Contains(entryName, StringComparison.OrdinalIgnoreCase))
            {
                score += 18;
            }
        }

        if (!string.IsNullOrWhiteSpace(degreeKey) && string.Equals(entry.Degree, degreeKey, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(entry.Degree) && !string.IsNullOrWhiteSpace(degreeKey))
        {
            var degreeSet = MatchTextHelper.NormalizeDegreeKey(entry.Degree);
            if (string.Equals(degreeSet, degreeKey, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }
        }

        return score;
    }

    private static string GetCatalogDisplayName(ProductCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ModelToken))
        {
            return entry.ModelToken;
        }

        if (!string.IsNullOrWhiteSpace(entry.BaseName))
        {
            return entry.BaseName;
        }

        return entry.ProductName;
    }

    private static string ResolveDegreeKey(OrderItem item)
    {
        var explicitFromRaw = MatchTextHelper.ExtractExplicitDegreeKey(item.RawText);
        var normalizedFromPowerSummary = MatchTextHelper.NormalizeDegreeKey(item.PowerSummary);

        if (!string.IsNullOrWhiteSpace(explicitFromRaw) && !string.IsNullOrWhiteSpace(normalizedFromPowerSummary))
        {
            var rawHasMultiple = explicitFromRaw.Contains('/', StringComparison.OrdinalIgnoreCase);
            var summaryHasMultiple = normalizedFromPowerSummary.Contains('/', StringComparison.OrdinalIgnoreCase);

            if (rawHasMultiple && !summaryHasMultiple)
            {
                return normalizedFromPowerSummary;
            }

            if (!rawHasMultiple && summaryHasMultiple)
            {
                return explicitFromRaw;
            }

            if (!rawHasMultiple && !summaryHasMultiple &&
                !string.Equals(explicitFromRaw, normalizedFromPowerSummary, StringComparison.OrdinalIgnoreCase))
            {
                return explicitFromRaw;
            }

            return normalizedFromPowerSummary;
        }

        if (!string.IsNullOrWhiteSpace(explicitFromRaw))
        {
            return explicitFromRaw;
        }

        if (!string.IsNullOrWhiteSpace(normalizedFromPowerSummary))
        {
            return normalizedFromPowerSummary;
        }

        return MatchTextHelper.NormalizeDegreeKey(item.RawText);
    }
}
