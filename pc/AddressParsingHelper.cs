using System.Text.RegularExpressions;

namespace WpfApp11;

internal static class AddressParsingHelper
{
    private static readonly Regex PlaceholderPrefixRegex = new(
        @"^(?:(?:null|undefined)\s*)+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PlaceholderOnlyRegex = new(
        @"^(?:null|undefined)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string NormalizeAddressInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        normalized = PlaceholderPrefixRegex.Replace(normalized, string.Empty).Trim();
        return PlaceholderOnlyRegex.IsMatch(normalized) ? string.Empty : normalized;
    }

    public static AddressParts SplitAddress(string? address)
    {
        var cleaned = NormalizeAddressInput(address);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return AddressParts.Empty;
        }

        const string markerPattern =
            @"^(?<state>.*?(?:省|自治区|特别行政区|市))?(?<city>.*?(?:市|自治州|地区|盟))?(?<district>.*?(?:区|县|旗|市|镇|乡|街道|苏木))?(?<detail>.*)$";
        var markerMatch = Regex.Match(cleaned, markerPattern);
        if (markerMatch.Success)
        {
            var state = markerMatch.Groups["state"].Value.Trim();
            var city = markerMatch.Groups["city"].Value.Trim();
            var district = markerMatch.Groups["district"].Value.Trim();
            var detail = markerMatch.Groups["detail"].Value.Trim();

            if (!string.IsNullOrWhiteSpace(state) ||
                !string.IsNullOrWhiteSpace(city) ||
                !string.IsNullOrWhiteSpace(district))
            {
                return new AddressParts(state, city, district, detail);
            }
        }

        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 4)
        {
            return new AddressParts(
                tokens[0],
                tokens[1],
                tokens[2],
                string.Join(' ', tokens, 3, tokens.Length - 3));
        }

        if (tokens.Length == 3)
        {
            return new AddressParts(tokens[0], tokens[1], tokens[2], string.Empty);
        }

        if (tokens.Length == 2)
        {
            return new AddressParts(tokens[0], tokens[1], string.Empty, string.Empty);
        }

        return new AddressParts(string.Empty, string.Empty, string.Empty, cleaned);
    }

    public static AddressParts ResolveRegionParts(string? receiverRegion, string? receiverAddress)
    {
        var regionParts = SplitAddress(receiverRegion);
        var addressParts = SplitAddress(receiverAddress);
        return CompareRegionCompleteness(addressParts, regionParts) > 0
            ? addressParts
            : regionParts;
    }

    public static string CombineRegion(string? state, string? city, string? district)
    {
        return NormalizeAddressInput($"{NormalizeAddressInput(state)}{NormalizeAddressInput(city)}{NormalizeAddressInput(district)}");
    }

    private static int CompareRegionCompleteness(AddressParts left, AddressParts right)
    {
        var leftScore = CountResolvedRegionSegments(left);
        var rightScore = CountResolvedRegionSegments(right);
        if (leftScore != rightScore)
        {
            return leftScore.CompareTo(rightScore);
        }

        var leftLength = CombineRegion(left.State, left.City, left.District).Length;
        var rightLength = CombineRegion(right.State, right.City, right.District).Length;
        return leftLength.CompareTo(rightLength);
    }

    private static int CountResolvedRegionSegments(AddressParts parts)
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(parts.State))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(parts.City))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(parts.District))
        {
            count++;
        }

        return count;
    }
}

internal readonly record struct AddressParts(string State, string City, string District, string Detail)
{
    public static AddressParts Empty => new(string.Empty, string.Empty, string.Empty, string.Empty);
}
