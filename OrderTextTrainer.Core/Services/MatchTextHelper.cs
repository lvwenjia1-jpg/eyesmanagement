using System.Text.RegularExpressions;

namespace OrderTextTrainer.Core.Services;

public static class MatchTextHelper
{
    private static readonly Regex CompactRegex = new(@"[\s,'""\[\](){}<>·,;:，；：\-/]", RegexOptions.Compiled);
    private static readonly Regex DegreeRegex = new(@"(?<!\d)(\d{1,4})(?!\d)", RegexOptions.Compiled);

    public static string Compact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return CompactRegex.Replace(text.Trim().ToLowerInvariant(), string.Empty);
    }

    public static string NormalizeDegreeKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var matches = DegreeRegex.Matches(text);
        if (matches.Count == 0)
        {
            return string.Empty;
        }

        var values = matches
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return values.Count == 1 ? values[0] : string.Join("/", values);
    }

    public static string ExtractTrailingDegree(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var match = Regex.Match(text.Trim(), @"(?<base>.*?)(?<degree>\d{1,4})$");
        return match.Success ? match.Groups["degree"].Value : string.Empty;
    }

    public static string RemoveTrailingDegree(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        var degree = ExtractTrailingDegree(trimmed);
        if (string.IsNullOrWhiteSpace(degree))
        {
            return trimmed;
        }

        return trimmed[..^degree.Length];
    }
}
