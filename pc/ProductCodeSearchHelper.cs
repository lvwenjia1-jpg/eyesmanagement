using System.Text.RegularExpressions;
using OrderTextTrainer.Core.Services;

namespace WpfApp11;

public static class ProductCodeSearchHelper
{
    public const int DefaultVisibleCount = 60;

    public const int MaxVisibleCount = int.MaxValue;

    private static readonly Regex ExplicitTrailingDegreeRegex = new(@"^(?<text>.*?)(?<degree>\d{1,4})\s*(?:度数|度)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MixedTrailingDegreeRegex = new(@"^(?<text>.*\D)\s*(?<degree>\d{1,4})\s*$", RegexOptions.Compiled);
    private static readonly Regex InlineDegreeNoiseRegex = new(@"(?<![A-Za-z])\d{1,4}(?![A-Za-z])\s*(?:度数|度)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ProductCodeFilterResult FilterOptions(IEnumerable<ProductCodeOption> options, string? keyword)
    {
        var normalized = NormalizeKeyword(keyword);
        var visibleLimit = string.IsNullOrWhiteSpace(normalized.RawKeyword) ? DefaultVisibleCount : MaxVisibleCount;
        var visible = new List<ProductCodeOption>();
        var totalMatches = 0;

        foreach (var option in options)
        {
            if (!Matches(option, normalized))
            {
                continue;
            }

            totalMatches++;
            if (visible.Count < visibleLimit)
            {
                visible.Add(option);
            }
        }

        return new ProductCodeFilterResult(visible, totalMatches, totalMatches > visible.Count);
    }

    public static ProductCodeSearchKeyword NormalizeKeyword(string? keyword)
    {
        var rawKeyword = keyword?.Trim() ?? string.Empty;
        var exactDegreeKey = TryExtractExactTrailingDegree(rawKeyword, out var textKeywordWithoutTrailingDegree)
            ? MatchTextHelper.NormalizeDegreeKey(textKeywordWithoutTrailingDegree.ExactDegreeSource)
            : string.Empty;

        var textKeyword = !string.IsNullOrWhiteSpace(exactDegreeKey)
            ? textKeywordWithoutTrailingDegree.TextKeyword
            : RemoveInlineDegreeNoiseForTextSearch(rawKeyword);
        var compactKeyword = MatchTextHelper.Compact(textKeyword);
        var initialKeyword = string.Concat(textKeyword.Where(ch => char.IsLetterOrDigit(ch) && ch <= '\u007F')).ToLowerInvariant();
        var terms = textKeyword
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '\uFF0C', '/', '|', ';', '\uFF1B' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ProductCodeSearchKeyword(rawKeyword, textKeyword, compactKeyword, initialKeyword, exactDegreeKey, terms);
    }

    public static bool Matches(ProductCodeOption option, ProductCodeSearchKeyword keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword.RawKeyword))
        {
            return option.SortOrder < DefaultVisibleCount;
        }

        if (!MatchesExactDegree(option, keyword.ExactDegreeKey))
        {
            return false;
        }

        if (MatchesTextFields(option, keyword))
        {
            return true;
        }

        return MatchesCodeFields(option, keyword);
    }

    private static bool MatchesExactDegree(ProductCodeOption option, string exactDegreeKey)
    {
        if (string.IsNullOrWhiteSpace(exactDegreeKey))
        {
            return true;
        }

        return string.Equals(
            MatchTextHelper.NormalizeDegreeKey(option.DegreeText),
            exactDegreeKey,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesTextFields(ProductCodeOption option, ProductCodeSearchKeyword keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword.TextKeyword))
        {
            return !string.IsNullOrWhiteSpace(keyword.ExactDegreeKey);
        }

        var generalSearchText = BuildGeneralSearchText(option);
        if (keyword.Terms.Count > 1 && keyword.Terms.All(term =>
                ContainsTextField(option, term) ||
                generalSearchText.Contains(MatchTextHelper.Compact(term), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(keyword.CompactKeyword) &&
            generalSearchText.Contains(keyword.CompactKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(keyword.InitialKeyword) &&
            option.Initials.Contains(keyword.InitialKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ContainsTextField(option, keyword.TextKeyword);
    }

    private static bool MatchesCodeFields(ProductCodeOption option, ProductCodeSearchKeyword keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword.RawKeyword))
        {
            return false;
        }

        return option.ProductCode.Contains(keyword.RawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.CoreCode.Contains(keyword.RawKeyword, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(keyword.CompactKeyword) &&
                (MatchTextHelper.Compact(option.ProductCode).Contains(keyword.CompactKeyword, StringComparison.OrdinalIgnoreCase) ||
                 MatchTextHelper.Compact(option.CoreCode).Contains(keyword.CompactKeyword, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ContainsTextField(ProductCodeOption option, string value)
    {
        return option.WearPeriod.Contains(value, StringComparison.OrdinalIgnoreCase) ||
               option.ModelName.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildGeneralSearchText(ProductCodeOption option)
    {
        return MatchTextHelper.Compact(string.Join(" ",
            option.ProductCode,
            option.CoreCode,
            option.WearPeriod,
            option.ModelName));
    }

    private static bool TryExtractExactTrailingDegree(string rawKeyword, out (string TextKeyword, string ExactDegreeSource) result)
    {
        result = (rawKeyword, string.Empty);
        if (string.IsNullOrWhiteSpace(rawKeyword))
        {
            return false;
        }

        var trimmed = rawKeyword.Trim();
        var explicitMatch = ExplicitTrailingDegreeRegex.Match(trimmed);
        if (explicitMatch.Success)
        {
            result = (explicitMatch.Groups["text"].Value.Trim(), explicitMatch.Groups["degree"].Value);
            return true;
        }

        var mixedMatch = MixedTrailingDegreeRegex.Match(trimmed);
        if (mixedMatch.Success)
        {
            result = (mixedMatch.Groups["text"].Value.Trim(), mixedMatch.Groups["degree"].Value);
            return true;
        }

        return false;
    }

    private static string RemoveInlineDegreeNoiseForTextSearch(string rawKeyword)
    {
        if (string.IsNullOrWhiteSpace(rawKeyword))
        {
            return string.Empty;
        }

        return InlineDegreeNoiseRegex.Replace(rawKeyword, " ").Trim();
    }
}

public sealed record ProductCodeSearchKeyword(
    string RawKeyword,
    string TextKeyword,
    string CompactKeyword,
    string InitialKeyword,
    string ExactDegreeKey,
    IReadOnlyList<string> Terms);

public sealed record ProductCodeFilterResult(
    IReadOnlyList<ProductCodeOption> VisibleOptions,
    int TotalMatches,
    bool IsTruncated);
