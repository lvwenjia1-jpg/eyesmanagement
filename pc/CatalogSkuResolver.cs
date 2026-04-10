using OrderTextTrainer.Core.Models;
using OrderTextTrainer.Core.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace WpfApp11;

public sealed class CatalogSkuResolver
{
    static CatalogSkuResolver()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void RefreshDraft(OrderDraft draft, WorkflowSettingsSnapshot snapshot)
    {
        foreach (var item in draft.Items)
        {
            RefreshItem(item, snapshot);
        }
    }

    public void RefreshItem(OrderItemDraft item, WorkflowSettingsSnapshot snapshot)
    {
        var catalog = snapshot.ProductCatalog
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ProductCode))
            .ToList();

        item.ProductCodeOptions = new List<ProductCodeOption>();

        item.DegreeOptions = catalog
            .Select(entry => Safe(entry.Degree))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => ParseDegree(value))
            .ToList();

        if (catalog.Count == 0)
        {
            item.MatchHint = "未导入商品列表，请先导入 Excel 商品表。";
            return;
        }

        var manualSelection = catalog.FirstOrDefault(entry =>
            string.Equals(entry.ProductCode, item.ProductCode, StringComparison.OrdinalIgnoreCase));
        if (manualSelection is not null)
        {
            ApplyCatalogEntry(item, manualSelection, snapshot, "已按商品编码确认。");
            item.DegreeOptions = BuildDegreeOptions(catalog.Where(entry =>
                string.Equals(entry.BaseName, manualSelection.BaseName, StringComparison.OrdinalIgnoreCase)));
            return;
        }

        var aliasFamily = FindAliasFamily(item, snapshot, catalog);
        var family = aliasFamily ?? FindTopFamily(item, catalog);
        item.ProductCodeOptions = BuildProductCodeOptions(catalog, family?.Entries);

        if (family is null || family.Entries.Count == 0)
        {
            item.MatchHint = "未匹配到商品族，请手工选择商品编码。";
            return;
        }

        item.DegreeOptions = BuildDegreeOptions(family.Entries);
        if (string.IsNullOrWhiteSpace(item.ProductName))
        {
            item.ProductName = GetFamilyDisplayName(family.Entries[0]);
        }

        var resolvedEntries = FilterByContext(family.Entries, item, snapshot).ToList();
        if (resolvedEntries.Count == 1)
        {
            ApplyCatalogEntry(item, resolvedEntries[0], snapshot, family.MatchNote);
            return;
        }

        item.ProductCode = string.Empty;
        if (string.IsNullOrWhiteSpace(item.WearPeriod))
        {
            var periods = family.Entries
                .Select(entry => ResolveCanonicalWearPeriod(entry.SpecificationToken, snapshot))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (periods.Count == 1)
            {
                item.WearPeriod = periods[0];
            }
        }

        item.MatchHint = resolvedEntries.Count > 1
            ? $"{family.MatchNote}，已锁定商品族，请补全周期/度数。"
            : $"{family.MatchNote}，请补全周期/度数后自动回填编码。";
    }

    private static CatalogFamilyMatch? FindAliasFamily(
        OrderItemDraft item,
        WorkflowSettingsSnapshot snapshot,
        IReadOnlyList<ProductCatalogEntry> catalog)
    {
        var compactTokens = BuildCompactTokens(item);
        var aliasMatch = snapshot.ProductCodeMappings
            .Where(row => !string.IsNullOrWhiteSpace(row.Alias) && !string.IsNullOrWhiteSpace(row.ProductCode))
            .Select(row => new
            {
                Row = row,
                Alias = MatchTextHelper.Compact(row.Alias),
                Score = compactTokens.Any(token => token.Contains(MatchTextHelper.Compact(row.Alias), StringComparison.OrdinalIgnoreCase))
                    ? MatchTextHelper.Compact(row.Alias).Length
                    : 0
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();

        if (aliasMatch is null)
        {
            return null;
        }

        var representative = catalog.FirstOrDefault(entry =>
            string.Equals(entry.ProductCode, aliasMatch.Row.ProductCode, StringComparison.OrdinalIgnoreCase));
        if (representative is null)
        {
            return null;
        }

        var familyEntries = catalog
            .Where(entry => string.Equals(entry.BaseName, representative.BaseName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (familyEntries.Count == 0)
        {
            return null;
        }

        return new CatalogFamilyMatch(familyEntries, $"编码对照表匹配：{aliasMatch.Row.Alias}");
    }

    private static CatalogFamilyMatch? FindTopFamily(OrderItemDraft item, IReadOnlyList<ProductCatalogEntry> catalog)
    {
        var compactTokens = BuildCompactTokens(item);
        if (compactTokens.Count == 0)
        {
            return null;
        }

        var directLooseFamily = FindDirectLooseFamily(item, catalog);
        if (directLooseFamily is not null)
        {
            return directLooseFamily;
        }

        var rankedFamilies = catalog
            .GroupBy(GetFamilyKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CatalogFamilyRank(
                group.ToList(),
                ScoreFamily(group.ToList(), compactTokens)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => MatchTextHelper.Compact(GetFamilyDisplayName(item.Entries[0])).Length)
            .Take(3)
            .ToList();

        if (rankedFamilies.Count == 0)
        {
            return null;
        }

        if (rankedFamilies.Count > 1 && rankedFamilies[0].Score == rankedFamilies[1].Score)
        {
            var looseFamilies = catalog
                .GroupBy(GetLooseFamilyKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CatalogFamilyRank(
                    group.ToList(),
                    ScoreFamily(group.ToList(), compactTokens)))
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => MatchTextHelper.Compact(GetFamilyDisplayName(item.Entries[0])).Length)
                .Take(3)
                .ToList();

            if (looseFamilies.Count == 0)
            {
                return null;
            }

            if (looseFamilies.Count > 1 && looseFamilies[0].Score == looseFamilies[1].Score)
            {
                return null;
            }

            return new CatalogFamilyMatch(looseFamilies[0].Entries, "商品列表已匹配到商品系列");
        }

        return new CatalogFamilyMatch(rankedFamilies[0].Entries, "商品列表已匹配到商品族");
    }

    private static CatalogFamilyMatch? FindDirectLooseFamily(OrderItemDraft item, IReadOnlyList<ProductCatalogEntry> catalog)
    {
        var directTokens = new[]
        {
            item.ProductName,
            RemoveDigits(item.ProductName)
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(MatchTextHelper.Compact)
        .Where(value => value.Length >= 4)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(value => value.Length)
        .ToList();

        foreach (var token in directTokens)
        {
            var exactGroups = catalog
                .GroupBy(GetLooseFamilyKey, StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key) &&
                                (string.Equals(group.Key, token, StringComparison.OrdinalIgnoreCase) ||
                                 group.Key.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                                 token.Contains(group.Key, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (exactGroups.Count == 1)
            {
                return new CatalogFamilyMatch(exactGroups[0].ToList(), "商品名已匹配到商品系列");
            }
        }

        return null;
    }

    private static IEnumerable<ProductCatalogEntry> FilterByContext(
        IReadOnlyList<ProductCatalogEntry> familyEntries,
        OrderItemDraft item,
        WorkflowSettingsSnapshot snapshot)
    {
        IEnumerable<ProductCatalogEntry> query = familyEntries;

        var degreeKey = MatchTextHelper.NormalizeDegreeKey(item.DegreeText);
        if (!string.IsNullOrWhiteSpace(degreeKey))
        {
            query = query.Where(entry => string.Equals(entry.Degree, degreeKey, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(item.WearPeriod))
        {
            query = query.Where(entry => IsWearCompatible(entry, item.WearPeriod, snapshot));
        }

        return query;
    }

    private static bool IsWearCompatible(ProductCatalogEntry entry, string wearPeriod, WorkflowSettingsSnapshot snapshot)
    {
        var left = MatchTextHelper.Compact(wearPeriod);
        var right = MatchTextHelper.Compact(entry.SpecificationToken);
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        if (left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
            right.Contains(left, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var matched = ResolveCanonicalWearPeriod(entry.SpecificationToken, snapshot);
        var canonical = MatchTextHelper.Compact(matched);
        return !string.IsNullOrWhiteSpace(canonical) &&
               (left.Contains(canonical, StringComparison.OrdinalIgnoreCase) ||
                canonical.Contains(left, StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyCatalogEntry(
        OrderItemDraft item,
        ProductCatalogEntry entry,
        WorkflowSettingsSnapshot snapshot,
        string note)
    {
        item.ProductCode = entry.ProductCode;
        item.ProductName = string.IsNullOrWhiteSpace(entry.ProductName) ? entry.ProductCode : entry.ProductName;
        item.BarcodeText = Safe(entry.Barcode);
        item.DegreeText = string.IsNullOrWhiteSpace(entry.Degree) ? item.DegreeText : entry.Degree;

        if (string.IsNullOrWhiteSpace(item.WearPeriod))
        {
            item.WearPeriod = ResolveCanonicalWearPeriod(entry.SpecificationToken, snapshot);
        }

        item.MatchHint = $"{note}，已自动回填商品编码。";
    }

    private static string ResolveCanonicalWearPeriod(string specificationToken, WorkflowSettingsSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(specificationToken))
        {
            return string.Empty;
        }

        var compactSpecification = MatchTextHelper.Compact(specificationToken);
        var directWearPeriod = snapshot.WearPeriods
            .Select(item => item.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderByDescending(value => MatchTextHelper.Compact(value).Length)
            .FirstOrDefault(value =>
            {
                var compactValue = MatchTextHelper.Compact(value);
                return compactSpecification.Contains(compactValue, StringComparison.OrdinalIgnoreCase) ||
                       compactValue.Contains(compactSpecification, StringComparison.OrdinalIgnoreCase);
            });

        if (!string.IsNullOrWhiteSpace(directWearPeriod))
        {
            return directWearPeriod;
        }

        var mapping = snapshot.WearPeriodMappings.FirstOrDefault(item =>
        {
            var compactAlias = MatchTextHelper.Compact(item.Alias);
            return !string.IsNullOrWhiteSpace(compactAlias) &&
                   (compactSpecification.Contains(compactAlias, StringComparison.OrdinalIgnoreCase) ||
                    compactAlias.Contains(compactSpecification, StringComparison.OrdinalIgnoreCase));
        });

        return !string.IsNullOrWhiteSpace(mapping?.WearPeriod) ? mapping.WearPeriod : specificationToken;
    }

    private static int ScoreFamily(IReadOnlyList<ProductCatalogEntry> entries, IReadOnlyList<string> compactTokens)
    {
        var sample = entries[0];
        var aliases = GetFamilyAliases(sample)
            .Select(MatchTextHelper.Compact)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var score = 0;

        foreach (var token in compactTokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            foreach (var alias in aliases)
            {
                if (token.Contains(alias, StringComparison.OrdinalIgnoreCase) ||
                    alias.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    score += alias.Length >= 6 ? 40 : 18;
                }
            }
        }

        return score;
    }

    private static List<string> BuildCompactTokens(OrderItemDraft item)
    {
        var tokens = new List<string>();
        var baseNames = BuildBaseSearchNames(item);
        var degrees = ExtractDegrees(item);

        foreach (var baseName in baseNames)
        {
            AddCompactToken(tokens, baseName);
            foreach (var degree in degrees)
            {
                AddCompactToken(tokens, $"{baseName}{degree}");
            }
        }

        AddCompactToken(tokens, item.ProductName);
        AddCompactToken(tokens, item.SourceText);

        return tokens
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildDegreeOptions(IEnumerable<ProductCatalogEntry> entries)
    {
        return entries
            .Select(entry => Safe(entry.Degree))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => ParseDegree(value))
            .ToList();
    }

    private static List<ProductCodeOption> BuildProductCodeOptions(
        IReadOnlyList<ProductCatalogEntry> catalog,
        IReadOnlyList<ProductCatalogEntry>? prioritizedEntries)
    {
        var prioritizedCodes = new HashSet<string>(
            (prioritizedEntries ?? Array.Empty<ProductCatalogEntry>())
                .Select(entry => entry.ProductCode),
            StringComparer.OrdinalIgnoreCase);

        var prioritized = (prioritizedEntries ?? Array.Empty<ProductCatalogEntry>())
            .GroupBy(entry => entry.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => GetCoreCode(entry), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => ParseDegree(Safe(entry.Degree)))
            .ThenBy(entry => entry.ProductCode, StringComparer.OrdinalIgnoreCase);

        var remaining = catalog
            .Where(entry => !prioritizedCodes.Contains(entry.ProductCode))
            .GroupBy(entry => entry.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => GetCoreCode(entry), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => ParseDegree(Safe(entry.Degree)))
            .ThenBy(entry => entry.ProductCode, StringComparer.OrdinalIgnoreCase);

        return prioritized
            .Concat(remaining)
            .Select(entry => new ProductCodeOption
            {
                ProductCode = entry.ProductCode,
                CoreCode = GetCoreCode(entry),
                DisplayText = BuildOptionDisplayText(entry),
                SearchText = BuildOptionSearchText(entry),
                Initials = BuildOptionInitials(entry)
            })
            .ToList();
    }

    private static string GetFamilyKey(ProductCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ModelToken))
        {
            return MatchTextHelper.Compact(entry.ModelToken);
        }

        if (!string.IsNullOrWhiteSpace(entry.BaseName))
        {
            return MatchTextHelper.Compact(entry.BaseName);
        }

        return MatchTextHelper.Compact(entry.ProductCode);
    }

    private static string GetCoreCode(ProductCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.SpecCode))
        {
            return entry.SpecCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.BaseName))
        {
            return entry.BaseName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.ModelToken))
        {
            return entry.ModelToken.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.ProductName))
        {
            return entry.ProductName.Trim();
        }

        return entry.ProductCode.Trim();
    }

    private static string BuildOptionDisplayText(ProductCatalogEntry entry)
    {
        var parts = new List<string> { GetCoreCode(entry), entry.ProductCode.Trim() };

        if (!string.IsNullOrWhiteSpace(entry.Degree))
        {
            parts.Add($"度数 {entry.Degree.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Barcode))
        {
            parts.Add(entry.Barcode.Trim());
        }

        return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildOptionSearchText(ProductCatalogEntry entry)
    {
        return MatchTextHelper.Compact(string.Join(" ",
            new[]
            {
                entry.ProductCode,
                entry.ProductName,
                entry.BaseName,
                entry.ModelToken,
                entry.SpecCode,
                entry.Barcode,
                entry.SearchText,
                GetCoreCode(entry)
            }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static string BuildOptionInitials(ProductCatalogEntry entry)
    {
        return string.Concat(new[]
            {
                entry.ProductCode,
                entry.ProductName,
                entry.BaseName,
                entry.ModelToken,
                GetCoreCode(entry)
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(ExtractInitials))
            .ToLowerInvariant();
    }

    private static string GetLooseFamilyKey(ProductCatalogEntry entry)
    {
        return MatchTextHelper.Compact(RemoveColorSuffix(RemoveSpecificationPrefix(GetFamilyDisplayName(entry), entry.SpecificationToken)));
    }

    private static string GetFamilyDisplayName(ProductCatalogEntry entry)
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

    private static IEnumerable<string> GetFamilyAliases(ProductCatalogEntry entry)
    {
        var displayName = GetFamilyDisplayName(entry);
        var looseAlias = RemoveColorSuffix(RemoveSpecificationPrefix(displayName, entry.SpecificationToken));
        var baseAlias = RemoveColorSuffix(RemoveSpecificationPrefix(entry.BaseName, entry.SpecificationToken));
        var productCodeAlias = RemoveSpecificationPrefix(entry.ProductCode, entry.SpecificationToken);
        var displayWithoutDegree = MatchTextHelper.RemoveTrailingDegree(displayName);
        var baseWithoutDegree = MatchTextHelper.RemoveTrailingDegree(entry.BaseName);
        var productCodeWithoutDegree = MatchTextHelper.RemoveTrailingDegree(productCodeAlias);

        foreach (var value in new[]
                 {
                     displayName,
                     entry.ProductName,
                     entry.BaseName,
                     entry.ProductCode,
                     RemoveSpecificationPrefix(entry.ProductName, entry.SpecificationToken),
                     RemoveSpecificationPrefix(entry.BaseName, entry.SpecificationToken),
                     productCodeAlias,
                     displayWithoutDegree,
                     baseWithoutDegree,
                     productCodeWithoutDegree,
                     looseAlias,
                     baseAlias
                 })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static string RemoveSpecificationPrefix(string? text, string? specificationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(specificationToken) &&
            text.StartsWith(specificationToken, StringComparison.OrdinalIgnoreCase))
        {
            return text[specificationToken.Length..].Trim();
        }

        return text.Trim();
    }

    private static string RemoveColorSuffix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text.Trim(), "(深蓝|浅蓝|棕|蓝|灰|粉|黄|绿|青|紫|黑|白|红|银|金)$", string.Empty, RegexOptions.IgnoreCase).Trim();
    }

    private static string RemoveDigits(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text, @"\d+", string.Empty).Trim();
    }

    private static List<string> BuildBaseSearchNames(OrderItemDraft item)
    {
        var names = new List<string>();
        foreach (var value in new[]
                 {
                     item.ProductName,
                     CleanupSearchText(item.SourceText)
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var cleaned = value.Trim();
            names.Add(cleaned);
            names.Add(RemoveDigits(cleaned));
            names.Add(MatchTextHelper.RemoveTrailingDegree(cleaned));
        }

        return names
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExtractDegrees(OrderItemDraft item)
    {
        var degreeSource = !string.IsNullOrWhiteSpace(item.DegreeText) ? item.DegreeText : item.SourceText;
        return Regex.Matches(degreeSource ?? string.Empty, @"(?<!\d)(\d{1,4})(?!\d)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CleanupSearchText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = text.Trim();
        cleaned = Regex.Replace(cleaned, @"^(款式|下单|商品|型号|品名|品牌)[:：]\s*", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"^\d+\s*", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(一个|两个|三个|一副|两副|一盒|两盒|一片|两片)", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\d{1,4}\s*度", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[xX]\d+", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[\/＋+].*$", string.Empty);
        return cleaned.Trim();
    }

    private static void AddCompactToken(ICollection<string> tokens, string? value)
    {
        var compact = MatchTextHelper.Compact(value);
        if (!string.IsNullOrWhiteSpace(compact))
        {
            tokens.Add(compact);
        }
    }

    private static string ExtractInitials(string text)
    {
        var builder = new StringBuilder();
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '/' or '\\' or '|' or '，' or ',' or '：' or ':')
            {
                continue;
            }

            if ((ch >= 'a' && ch <= 'z') ||
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9'))
            {
                builder.Append(char.ToLowerInvariant(ch));
                continue;
            }

            var initial = GetChineseInitial(ch);
            if (initial != '\0')
            {
                builder.Append(initial);
            }
        }

        return builder.ToString();
    }

    private static char GetChineseInitial(char ch)
    {
        try
        {
            var bytes = Encoding.GetEncoding("GB2312").GetBytes(ch.ToString());
            if (bytes.Length < 2)
            {
                return '\0';
            }

            var code = bytes[0] * 256 + bytes[1] - 65536;
            return code switch
            {
                >= -20319 and <= -20284 => 'a',
                >= -20283 and <= -19776 => 'b',
                >= -19775 and <= -19219 => 'c',
                >= -19218 and <= -18711 => 'd',
                >= -18710 and <= -18527 => 'e',
                >= -18526 and <= -18240 => 'f',
                >= -18239 and <= -17923 => 'g',
                >= -17922 and <= -17418 => 'h',
                >= -17417 and <= -16475 => 'j',
                >= -16474 and <= -16213 => 'k',
                >= -16212 and <= -15641 => 'l',
                >= -15640 and <= -15166 => 'm',
                >= -15165 and <= -14923 => 'n',
                >= -14922 and <= -14915 => 'o',
                >= -14914 and <= -14631 => 'p',
                >= -14630 and <= -14150 => 'q',
                >= -14149 and <= -14091 => 'r',
                >= -14090 and <= -13319 => 's',
                >= -13318 and <= -12839 => 't',
                >= -12838 and <= -12557 => 'w',
                >= -12556 and <= -11848 => 'x',
                >= -11847 and <= -11056 => 'y',
                >= -11055 and <= -10247 => 'z',
                _ => '\0'
            };
        }
        catch
        {
            return '\0';
        }
    }

    private static int ParseDegree(string value)
    {
        return int.TryParse(value, out var degree) ? degree : int.MaxValue;
    }

    private static string Safe(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private sealed record CatalogFamilyMatch(IReadOnlyList<ProductCatalogEntry> Entries, string MatchNote);

    private sealed record CatalogFamilyRank(IReadOnlyList<ProductCatalogEntry> Entries, int Score);
}
