using System.IO.Compression;
using System.Xml.Linq;
using OrderTextTrainer.Core.Models;

namespace OrderTextTrainer.Core.Services;

public sealed class ProductCatalogXlsxReader
{
    public IReadOnlyList<ProductCatalogEntry> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("商品表不存在。", path);
        }

        using var archive = ZipFile.OpenRead(path);
        var sharedStrings = LoadSharedStrings(archive);
        var rows = new List<ProductCatalogEntry>();

        foreach (var sheetEntry in archive.Entries
                     .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            using var stream = sheetEntry.Open();
            var document = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var headerMap = new Dictionary<int, string>();
            var headerRowSeen = false;
            var hasStructuredHeader = false;

            foreach (var row in document.Root?.Element(ns + "sheetData")?.Elements(ns + "row") ?? Enumerable.Empty<XElement>())
            {
                var cells = ReadRow(row, sharedStrings, ns);
                if (!headerRowSeen)
                {
                    foreach (var cell in cells)
                    {
                        headerMap[cell.Key] = cell.Value;
                    }

                    headerRowSeen = true;
                    hasStructuredHeader = LooksLikeCatalogHeader(cells.Values);
                    if (hasStructuredHeader)
                    {
                        continue;
                    }
                }

                var productCode = hasStructuredHeader
                    ? GetCellValue(cells, headerMap, "商品编码")
                    : GetFirstColumnValue(cells);
                var encodedCode = hasStructuredHeader ? GetCellValue(cells, headerMap, "编码") : string.Empty;
                var productName = hasStructuredHeader ? GetCellValue(cells, headerMap, "商品名称") : string.Empty;
                var specCode = hasStructuredHeader ? GetCellValue(cells, headerMap, "规格编码") : string.Empty;
                var barcode = hasStructuredHeader ? GetCellValue(cells, headerMap, "条码") : string.Empty;

                productCode = string.IsNullOrWhiteSpace(productCode) ? encodedCode : productCode;
                if (!hasStructuredHeader && LooksLikeHeaderText(productCode))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(productCode))
                {
                    continue;
                }

                productCode = productCode.Trim();
                var degree = MatchTextHelper.ExtractTrailingDegree(productCode);
                var baseName = MatchTextHelper.RemoveTrailingDegree(productCode);
                if (string.IsNullOrWhiteSpace(baseName))
                {
                    baseName = string.IsNullOrWhiteSpace(productName) ? productCode : productName.Trim();
                }

                var specificationToken = ExtractSpecificationToken(baseName);
                var modelToken = string.IsNullOrWhiteSpace(specificationToken) || specificationToken.Length >= baseName.Length
                    ? baseName
                    : baseName[specificationToken.Length..];

                rows.Add(new ProductCatalogEntry
                {
                    ProductCode = productCode,
                    ProductName = productCode,
                    SpecCode = specCode?.Trim() ?? string.Empty,
                    Barcode = barcode?.Trim() ?? string.Empty,
                    BaseName = baseName?.Trim() ?? string.Empty,
                    SpecificationToken = specificationToken,
                    ModelToken = modelToken?.Trim() ?? string.Empty,
                    Degree = degree?.Trim() ?? string.Empty,
                    SearchText = MatchTextHelper.Compact($"{productCode} {specificationToken} {modelToken} {degree}")
                });
            }
        }

        return rows
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ProductCode))
            .GroupBy(entry => entry.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static bool LooksLikeCatalogHeader(IEnumerable<string> values)
    {
        var headerSet = values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return headerSet.Any(value => value.Contains("商品编码", StringComparison.OrdinalIgnoreCase)) ||
               headerSet.Any(value => value.Contains("编码", StringComparison.OrdinalIgnoreCase)) ||
               headerSet.Any(value => value.Contains("商品名称", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> LoadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return Array.Empty<string>();
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        return document.Root?
                   .Elements(ns + "si")
                   .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value)))
                   .ToList()
               ?? new List<string>();
    }

    private static Dictionary<int, string> ReadRow(XElement row, IReadOnlyList<string> sharedStrings, XNamespace ns)
    {
        var values = new Dictionary<int, string>();
        foreach (var cell in row.Elements(ns + "c"))
        {
            var columnIndex = GetColumnIndex((string?)cell.Attribute("r"));
            if (columnIndex < 0)
            {
                continue;
            }

            values[columnIndex] = ReadCellValue(cell, sharedStrings, ns);
        }

        return values;
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings, XNamespace ns)
    {
        var type = (string?)cell.Attribute("t");
        if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell.Descendants(ns + "t").Select(text => text.Value));
        }

        var value = cell.Element(ns + "v")?.Value ?? string.Empty;
        if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value, out var sharedIndex) &&
            sharedIndex >= 0 &&
            sharedIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedIndex];
        }

        return value;
    }

    private static string GetCellValue(IReadOnlyDictionary<int, string> cells, IReadOnlyDictionary<int, string> headers, string headerName)
    {
        var columnIndex = headers
            .FirstOrDefault(item => IsHeaderMatch(item.Value, headerName))
            .Key;

        return columnIndex != 0 && cells.TryGetValue(columnIndex, out var value)
            ? value.Trim()
            : string.Empty;
    }

    private static string GetFirstColumnValue(IReadOnlyDictionary<int, string> cells)
    {
        return cells.TryGetValue(1, out var value) ? value.Trim() : string.Empty;
    }

    private static bool LooksLikeHeaderText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        return trimmed.Contains("商品编码", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "编码", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Contains("商品信息", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHeaderMatch(string actualHeader, string expectedHeader)
    {
        if (string.IsNullOrWhiteSpace(actualHeader))
        {
            return false;
        }

        var actual = actualHeader.Trim();
        return string.Equals(actual, expectedHeader, StringComparison.OrdinalIgnoreCase) ||
               actual.Contains(expectedHeader, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return -1;
        }

        var index = 0;
        foreach (var ch in cellReference.TakeWhile(char.IsLetter))
        {
            index *= 26;
            index += char.ToUpperInvariant(ch) - 'A' + 1;
        }

        return index;
    }

    private static string ExtractSpecificationToken(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return string.Empty;
        }

        var markerIndex = baseName.LastIndexOf('片');
        if (markerIndex >= 0)
        {
            return baseName[..(markerIndex + 1)];
        }

        markerIndex = baseName.LastIndexOf('抛');
        if (markerIndex >= 0)
        {
            return baseName[..(markerIndex + 1)];
        }

        return string.Empty;
    }
}
