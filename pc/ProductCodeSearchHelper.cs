using OrderTextTrainer.Core.Services;

namespace WpfApp11;

public static class ProductCodeSearchHelper
{
    public const int DefaultVisibleCount = 60;

    public const int MaxVisibleCount = 200;

    public static ProductCodeFilterResult FilterOptions(IEnumerable<ProductCodeOption> options, string? keyword)
    {
        var normalized = NormalizeKeyword(keyword);
        var visibleLimit = string.IsNullOrWhiteSpace(normalized.RawKeyword) ? DefaultVisibleCount : MaxVisibleCount;
        var visible = new List<ProductCodeOption>(Math.Min(visibleLimit, DefaultVisibleCount));
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
        var compactKeyword = MatchTextHelper.Compact(rawKeyword);
        var initialKeyword = string.Concat(rawKeyword.Where(ch => char.IsLetterOrDigit(ch) && ch <= '\u007F')).ToLowerInvariant();
        var terms = rawKeyword
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '\uFF0C', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ProductCodeSearchKeyword(rawKeyword, compactKeyword, initialKeyword, terms);
    }

    public static bool Matches(ProductCodeOption option, ProductCodeSearchKeyword keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword.RawKeyword))
        {
            return option.SortOrder < DefaultVisibleCount;
        }

        if (keyword.Terms.Count > 1 && keyword.Terms.All(term =>
                option.DisplayText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                option.SearchText.Contains(MatchTextHelper.Compact(term), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(keyword.CompactKeyword) &&
            option.SearchText.Contains(keyword.CompactKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(keyword.InitialKeyword) &&
            option.Initials.Contains(keyword.InitialKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return option.DisplayText.Contains(keyword.RawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.ProductCode.Contains(keyword.RawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.CoreCode.Contains(keyword.RawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.WearPeriod.Contains(keyword.RawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.ModelName.Contains(keyword.RawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.DegreeText.Contains(keyword.RawKeyword, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ProductCodeSearchKeyword(
    string RawKeyword,
    string CompactKeyword,
    string InitialKeyword,
    IReadOnlyList<string> Terms);

public sealed record ProductCodeFilterResult(
    IReadOnlyList<ProductCodeOption> VisibleOptions,
    int TotalMatches,
    bool IsTruncated);
