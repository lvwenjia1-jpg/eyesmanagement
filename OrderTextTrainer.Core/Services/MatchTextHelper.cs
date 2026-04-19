using System.Text.RegularExpressions;

namespace OrderTextTrainer.Core.Services;

public static class MatchTextHelper
{
    private static readonly Regex CompactRegex = new("[-\\s,'\"\\[\\](){}<>\\u00B7,;:\\uFF0C\\uFF1B\\uFF1A/]", RegexOptions.Compiled);
    private static readonly Regex DegreeRegex = new(@"(?<!\d)(\d{1,4})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex ExplicitDegreeRegex = new("(?<!\\d)(\\d{1,4})\\s*(?:\\u5EA6\\u6570|\\u5EA6)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NumericNoiseRegex = new(
        @"(?:\d+\s*(?:片装|片|副|幅|付|盒|个|支|套)|[xX×*＊]\s*\d+|共\s*\d+\s*(?:副|幅|付|盒|个|片))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        var sanitized = RemoveNonDegreeNumericNoise(text);
        var matches = DegreeRegex.Matches(sanitized);
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

    public static string ExtractExplicitDegreeKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var matches = ExplicitDegreeRegex.Matches(text);
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

    /// <summary>
    /// Removes numeric package/count fragments such as "10片" or "x2" so downstream degree
    /// extraction keeps only actual lens powers from dense product text.
    /// </summary>
    public static string RemoveNonDegreeNumericNoise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return NumericNoiseRegex.Replace(text, " ");
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
