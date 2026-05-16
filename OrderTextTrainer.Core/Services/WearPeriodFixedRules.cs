using System.Text.RegularExpressions;

namespace OrderTextTrainer.Core.Services;

public static class WearPeriodFixedRules
{
    private static readonly Regex DailyTenPieceRegex = new(
        @"(?:日抛|日拋)\s*(?:10片|十片|10片装|十片装|10片裝|十片裝)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DailyTwoPieceRegex = new(
        @"(?:日抛|日拋)\s*(?:2片|两片|2片装|两片装|2片裝|兩片裝)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DailyRegex = new(
        @"(?:日抛|日拋)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TrialRegex = new(
        @"(?:试戴片?|試戴片?|试用|試用)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HalfYearRegex = new(
        @"(?:半年抛|半年拋|半抛|半拋)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex YearRegex = new(
        @"(?:年抛|年拋)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool ContainsExplicitTenPieceDailyCue(string? source)
    {
        var text = Safe(source);
        return !string.IsNullOrWhiteSpace(text) && DailyTenPieceRegex.IsMatch(text);
    }

    public static string MatchExplicitCanonicalWearPeriod(string? source)
    {
        var text = Safe(source);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var hasTrialCue = TrialRegex.IsMatch(text);
        var hasDailyCue = DailyRegex.IsMatch(text);

        if (hasTrialCue && hasDailyCue)
        {
            return "日抛2片";
        }

        if (DailyTenPieceRegex.IsMatch(text))
        {
            return "日抛10片";
        }

        if (DailyTwoPieceRegex.IsMatch(text))
        {
            return "日抛2片";
        }

        if (HalfYearRegex.IsMatch(text))
        {
            return "半年抛";
        }

        if (YearRegex.IsMatch(text))
        {
            return "年抛";
        }

        if (hasTrialCue)
        {
            return "试戴片";
        }

        if (hasDailyCue)
        {
            return "日抛2片";
        }

        return string.Empty;
    }

    public static string NormalizeConfiguredWearPeriod(string? wearPeriod)
    {
        var text = Safe(wearPeriod);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var canonical = MatchExplicitCanonicalWearPeriod(text);
        return string.IsNullOrWhiteSpace(canonical) ? text : canonical;
    }

    private static string Safe(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
