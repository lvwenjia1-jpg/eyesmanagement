using OrderTextTrainer.Core.Services;
using System.Text.RegularExpressions;

namespace WpfApp11;

public static class ProductCodeSearchHelper
{
    public static List<ProductCodeOption> FilterOptions(IEnumerable<ProductCodeOption> options, string? keyword)
    {
        var rawKeyword = keyword?.Trim() ?? string.Empty;
        var compactKeyword = MatchTextHelper.Compact(rawKeyword);
        var initialKeyword = Regex.Replace(rawKeyword, @"[^A-Za-z0-9]", string.Empty).ToLowerInvariant();
        var terms = Regex.Split(rawKeyword, @"[\s,，/]+")
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .ToArray();

        return options
            .Where(option => Matches(option, rawKeyword, compactKeyword, initialKeyword, terms))
            .ToList();
    }

    public static bool Matches(
        ProductCodeOption option,
        string rawKeyword,
        string compactKeyword,
        string initialKeyword,
        IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(rawKeyword))
        {
            return option.SortOrder < 60;
        }

        if (terms.Count > 1 && terms.All(term =>
                option.DisplayText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                option.SearchText.Contains(MatchTextHelper.Compact(term), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(compactKeyword) &&
            option.SearchText.Contains(compactKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(compactKeyword) &&
            (MatchTextHelper.Compact(option.WearPeriod).Contains(compactKeyword, StringComparison.OrdinalIgnoreCase) ||
             MatchTextHelper.Compact(option.ModelName).Contains(compactKeyword, StringComparison.OrdinalIgnoreCase) ||
             MatchTextHelper.Compact(option.DegreeText).Contains(compactKeyword, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(initialKeyword) &&
            option.Initials.Contains(initialKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return option.DisplayText.Contains(rawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.ProductCode.Contains(rawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.CoreCode.Contains(rawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.WearPeriod.Contains(rawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.ModelName.Contains(rawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.DegreeText.Contains(rawKeyword, StringComparison.OrdinalIgnoreCase);
    }
}
