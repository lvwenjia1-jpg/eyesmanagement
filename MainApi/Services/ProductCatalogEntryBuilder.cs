using System.Text.RegularExpressions;
using MainApi.Domain;

namespace MainApi.Services;

public static class ProductCatalogEntryBuilder
{
    private static readonly Regex CompactRegex = new("[-\\s,'\"\\[\\](){}<>\\u00B7,;:\\uFF0C\\uFF1B\\uFF1A/]", RegexOptions.Compiled);
    private static readonly Regex TrailingDegreeRegex = new(@"(?<base>.*?)(?<degree>\d{1,4})$", RegexOptions.Compiled);
    private static readonly Regex DailySpecificationRegex = new(
        @"^(日抛\s*(?:\d+|[一二两三四五六七八九十百]+)\s*片(?:装)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ProductCatalogEntryRecord Build(string productCode, string? barcode, int sortOrder, DateTime updatedAtUtc)
    {
        return Build(new ProductCatalogBuildInput
        {
            ProductCode = productCode,
            Barcode = barcode ?? string.Empty
        }, sortOrder, updatedAtUtc);
    }

    public static ProductCatalogEntryRecord Build(ProductCatalogBuildInput input, int sortOrder, DateTime updatedAtUtc)
    {
        var normalizedCode = Safe(input.ProductCode);
        var normalizedSpecToken = Safe(input.SpecificationToken);
        var normalizedModelToken = Safe(input.ModelToken);
        var normalizedDegree = Safe(input.Degree);
        var normalizedBarcode = Safe(input.Barcode);
        var normalizedSpecCode = Safe(input.SpecCode);
        var normalizedProductName = Safe(input.ProductName);
        var isOutOfStock = input.IsOutOfStock;

        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            normalizedCode = BuildProductCode(normalizedSpecToken, normalizedModelToken, normalizedDegree);
        }

        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            throw new InvalidOperationException("Product code is required.");
        }

        if (string.IsNullOrWhiteSpace(normalizedDegree))
        {
            normalizedDegree = ExtractTrailingDegree(normalizedCode);
        }

        if (string.IsNullOrWhiteSpace(normalizedSpecToken) || string.IsNullOrWhiteSpace(normalizedModelToken))
        {
            var baseNameFromCode = RemoveTrailingDegree(normalizedCode);
            var inferredSpecToken = ExtractSpecificationToken(baseNameFromCode);
            if (string.IsNullOrWhiteSpace(normalizedSpecToken))
            {
                normalizedSpecToken = inferredSpecToken;
            }

            if (string.IsNullOrWhiteSpace(normalizedModelToken))
            {
                normalizedModelToken = string.IsNullOrWhiteSpace(normalizedSpecToken) || normalizedSpecToken.Length >= baseNameFromCode.Length
                    ? baseNameFromCode
                    : baseNameFromCode[normalizedSpecToken.Length..].Trim();
            }
        }

        var baseName = BuildBaseName(normalizedSpecToken, normalizedModelToken, normalizedCode);
        var finalProductName = string.IsNullOrWhiteSpace(normalizedProductName) ? normalizedCode : normalizedProductName;

        return new ProductCatalogEntryRecord
        {
            ProductCode = normalizedCode,
            ProductName = finalProductName,
            SpecCode = normalizedSpecCode,
            Barcode = normalizedBarcode,
            BaseName = baseName,
            SpecificationToken = normalizedSpecToken,
            ModelToken = normalizedModelToken,
            Degree = normalizedDegree,
            IsOutOfStock = isOutOfStock,
            SearchText = Compact($"{normalizedCode} {finalProductName} {normalizedSpecToken} {normalizedModelToken} {normalizedDegree} {normalizedBarcode}"),
            SortOrder = sortOrder,
            UpdatedAtUtc = updatedAtUtc
        };
    }

    public static string Compact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return CompactRegex.Replace(text.Trim().ToLowerInvariant(), string.Empty);
    }

    private static string ExtractTrailingDegree(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var match = TrailingDegreeRegex.Match(text.Trim());
        return match.Success ? match.Groups["degree"].Value : string.Empty;
    }

    private static string RemoveTrailingDegree(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        var degree = ExtractTrailingDegree(trimmed);
        return string.IsNullOrWhiteSpace(degree)
            ? trimmed
            : trimmed[..^degree.Length].Trim();
    }

    private static string ExtractSpecificationToken(string? baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return string.Empty;
        }

        var dailyMatch = DailySpecificationRegex.Match(baseName);
        if (dailyMatch.Success)
        {
            return dailyMatch.Groups[1].Value.Trim();
        }

        var markerIndex = baseName.LastIndexOf("片", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            return baseName[..(markerIndex + 1)].Trim();
        }

        markerIndex = baseName.LastIndexOf("抛", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            return baseName[..(markerIndex + 1)].Trim();
        }

        return string.Empty;
    }

    private static string Safe(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string BuildBaseName(string specificationToken, string modelToken, string fallbackCode)
    {
        if (ModelAlreadyContainsSpecification(specificationToken, modelToken))
        {
            return string.IsNullOrWhiteSpace(modelToken) ? fallbackCode : modelToken.Trim();
        }

        var baseName = string.IsNullOrWhiteSpace(specificationToken) && string.IsNullOrWhiteSpace(modelToken)
            ? fallbackCode
            : $"{specificationToken}{modelToken}".Trim();

        return string.IsNullOrWhiteSpace(baseName) ? fallbackCode : baseName;
    }

    private static bool ModelAlreadyContainsSpecification(string specificationToken, string modelToken)
    {
        var normalizedSpecificationToken = Compact(specificationToken);
        var normalizedModelToken = Compact(modelToken);
        if (string.IsNullOrWhiteSpace(normalizedSpecificationToken) || string.IsNullOrWhiteSpace(normalizedModelToken))
        {
            return false;
        }

        return normalizedModelToken.Contains(normalizedSpecificationToken, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildProductCode(string specificationToken, string modelToken, string degree)
    {
        var builder = $"{specificationToken}{modelToken}".Trim();
        if (string.IsNullOrWhiteSpace(builder))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(degree) ? builder : $"{builder}{degree}";
    }
}

public sealed class ProductCatalogBuildInput
{
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string SpecCode { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;

    public bool IsOutOfStock { get; set; }
}
