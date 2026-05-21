using System.Text.RegularExpressions;

namespace WpfApp11;

internal static class AddressParsingHelper
{
    public static string NormalizeAddressInput(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value, @"\s+", " ").Trim();
    }

    public static AddressParts SplitAddress(string? address)
    {
        var cleaned = NormalizeAddressInput(address);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return AddressParts.Empty;
        }

        const string markerPattern =
            @"^(?<state>.*?(?:省|自治区|特别行政区|市))?(?<city>.*?(?:市|自治州|地区|盟))?(?<district>.*?(?:区|县|旗|市))?(?<detail>.*)$";
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
}

internal readonly record struct AddressParts(string State, string City, string District, string Detail)
{
    public static AddressParts Empty => new(string.Empty, string.Empty, string.Empty, string.Empty);
}
