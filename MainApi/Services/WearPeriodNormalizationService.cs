using System.Text.RegularExpressions;
using MainApi.Contracts;

namespace MainApi.Services;

public sealed class WearPeriodNormalizationService
{
    private static readonly Regex TrailingDegreeRegex = new(@"(?<base>.*?)(?<degree>\d{1,4})$", RegexOptions.Compiled);
    private static readonly Regex BrandPrefixRegex = new(@"^(lenspop|leea|莉亚)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public NormalizedWearPeriodTokens NormalizeCatalogTokens(
        string? specificationToken,
        string? modelToken,
        string? productCode,
        string? productName,
        WearPeriodSettingsResponse settings)
    {
        var normalizedSpecificationToken = ResolveCanonicalWearPeriod(
            specificationToken,
            productCode,
            productName,
            modelToken,
            settings);

        var normalizedModelToken = ResolveModelToken(
            modelToken,
            productCode,
            productName,
            normalizedSpecificationToken);

        return new NormalizedWearPeriodTokens
        {
            SpecificationToken = normalizedSpecificationToken,
            ModelToken = normalizedModelToken
        };
    }

    public string NormalizeWearPeriod(string? value, WearPeriodSettingsResponse settings)
    {
        var normalized = Safe(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var direct = settings.WearPeriods
            .Select(item => Safe(item.Value))
            .FirstOrDefault(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var compactValue = Compact(normalized);
        var alias = settings.WearPeriodMappings
            .Where(item => !string.IsNullOrWhiteSpace(item.Alias) && !string.IsNullOrWhiteSpace(item.WearPeriod))
            .OrderByDescending(item => Compact(item.Alias).Length)
            .FirstOrDefault(item =>
            {
                var compactAlias = Compact(item.Alias);
                return string.Equals(compactValue, compactAlias, StringComparison.OrdinalIgnoreCase) ||
                       compactValue.Contains(compactAlias, StringComparison.OrdinalIgnoreCase);
            });

        return string.IsNullOrWhiteSpace(alias?.WearPeriod)
            ? normalized
            : alias.WearPeriod.Trim();
    }

    private string ResolveCanonicalWearPeriod(
        string? specificationToken,
        string? productCode,
        string? productName,
        string? modelToken,
        WearPeriodSettingsResponse settings)
    {
        foreach (var source in new[] { specificationToken, productName, productCode, modelToken })
        {
            var explicitWearPeriod = MatchExplicitWearPeriod(source);
            if (!string.IsNullOrWhiteSpace(explicitWearPeriod))
            {
                return NormalizeWearPeriod(explicitWearPeriod, settings);
            }

            var mapped = NormalizeWearPeriod(source, settings);
            if (!string.IsNullOrWhiteSpace(mapped) &&
                !string.Equals(mapped, Safe(source), StringComparison.OrdinalIgnoreCase))
            {
                return mapped;
            }
        }

        var allSources = string.Join(" ", new[] { specificationToken, productCode, productName, modelToken }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (ShouldDefaultLenspopToHalfYear(allSources))
        {
            return NormalizeWearPeriod("半年抛", settings);
        }

        return Safe(specificationToken);
    }

    private static string ResolveModelToken(
        string? modelToken,
        string? productCode,
        string? productName,
        string normalizedSpecificationToken)
    {
        var preferred = Safe(modelToken);
        if (string.IsNullOrWhiteSpace(preferred))
        {
            preferred = Safe(productName);
        }

        if (string.IsNullOrWhiteSpace(preferred))
        {
            preferred = RemoveTrailingDegree(Safe(productCode));
        }

        preferred = BrandPrefixRegex.Replace(preferred, string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSpecificationToken) &&
            preferred.StartsWith(normalizedSpecificationToken, StringComparison.OrdinalIgnoreCase))
        {
            preferred = preferred[normalizedSpecificationToken.Length..].Trim();
        }

        return preferred;
    }

    private static string MatchExplicitWearPeriod(string? source)
    {
        var text = Safe(source);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (ContainsExplicitTenPieceDailyCue(text))
        {
            return "日抛10片";
        }

        if (text.Contains("日抛2片", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("日抛两片", StringComparison.OrdinalIgnoreCase))
        {
            return "日抛2片";
        }

        if (text.Contains("半年抛", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("半抛", StringComparison.OrdinalIgnoreCase))
        {
            return "半年抛";
        }

        if (text.Contains("年抛", StringComparison.OrdinalIgnoreCase))
        {
            return "年抛";
        }

        if (text.Contains("试戴", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("试用", StringComparison.OrdinalIgnoreCase))
        {
            return "试戴片";
        }

        if (text.Contains("日抛", StringComparison.OrdinalIgnoreCase))
        {
            return "日抛2片";
        }

        return string.Empty;
    }

    private static bool ContainsExplicitTenPieceDailyCue(string? source)
    {
        var text = Safe(source);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("日抛10片", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("日抛十片", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("日抛10片装", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("日抛十片装", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(text, @"日抛\s*(?:10片|十片|10片装|十片装)", RegexOptions.IgnoreCase);
    }

    private static bool ShouldDefaultLenspopToHalfYear(string? source)
    {
        var text = Safe(source);
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("lenspop", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !(
            text.Contains("日抛", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("半年抛", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("半抛", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("年抛", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("月抛", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("季抛", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("试戴", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("试用", StringComparison.OrdinalIgnoreCase));
    }

    private static string Compact(string? value)
    {
        return ProductCatalogEntryBuilder.Compact(value);
    }

    private static string RemoveTrailingDegree(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var match = TrailingDegreeRegex.Match(value.Trim());
        return match.Success ? match.Groups["base"].Value.Trim() : value.Trim();
    }

    private static string Safe(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}

public sealed class NormalizedWearPeriodTokens
{
    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;
}
