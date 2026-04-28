using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OrderTextTrainer.Core.Models;

namespace OrderTextTrainer.Core.Services;

public sealed class ProductCatalogXlsxReader
{
    private static readonly Regex DailySpecificationRegex = new(
        @"^(日抛\s*(?:\d+|[一二两三四五六七八九十百]+)\s*片(?:装)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] ProductCodeHeaders = { "商品编码", "编码名称", "编码", "productcode", "code" };
    private static readonly string[] BarcodeHeaders = { "条码", "barcode", "barcodecode" };
    private static readonly string[] SpecificationHeaders = { "周期", "规格", "specificationtoken", "wearperiod", "period" };
    private static readonly string[] ModelHeaders = { "型号", "款式", "modeltoken", "model", "basename" };
    private static readonly string[] DegreeHeaders = { "度数", "degree" };

    public IReadOnlyList<ProductCatalogEntry> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"商品表不存在：{path}", path);
        }

        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("商品表仅支持 .xlsx 格式，请使用标准模板后重新导入。");
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var sharedStrings = LoadSharedStrings(archive);
            var sheetEntry = archive.Entries
                .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (sheetEntry is null)
            {
                throw new InvalidOperationException("Excel 文件为空，未找到可读取的工作表。");
            }

            using var stream = sheetEntry.Open();
            var document = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sheetRows = document.Root?.Element(ns + "sheetData")?.Elements(ns + "row").ToList() ?? new List<XElement>();
            if (sheetRows.Count == 0)
            {
                throw new InvalidOperationException("Excel 文件为空，请至少保留表头和一行商品数据。");
            }

            var headerMap = ReadRow(sheetRows[0], sharedStrings, ns);
            var productCodeColumn = FindColumnIndex(headerMap, ProductCodeHeaders);
            var barcodeColumn = FindColumnIndex(headerMap, BarcodeHeaders);
            var specificationColumn = FindColumnIndex(headerMap, SpecificationHeaders);
            var modelColumn = FindColumnIndex(headerMap, ModelHeaders);
            var degreeColumn = FindColumnIndex(headerMap, DegreeHeaders);

            if (productCodeColumn < 0 && (specificationColumn < 0 || modelColumn < 0))
            {
                throw new InvalidOperationException("Excel 缺少有效表头。标准模板请使用“商品编码”和“条码”两列；兼容模板至少需要“周期”和“型号”列。");
            }

            var rows = new List<ProductCatalogEntry>();
            var invalidRows = new List<int>();

            for (var rowIndex = 1; rowIndex < sheetRows.Count; rowIndex++)
            {
                var cells = ReadRow(sheetRows[rowIndex], sharedStrings, ns);
                var productCode = GetCellValue(cells, productCodeColumn);
                var barcode = GetCellValue(cells, barcodeColumn);
                var specificationToken = GetCellValue(cells, specificationColumn);
                var modelToken = GetCellValue(cells, modelColumn);
                var degree = GetCellValue(cells, degreeColumn);

                if (string.IsNullOrWhiteSpace(productCode) &&
                    string.IsNullOrWhiteSpace(barcode) &&
                    string.IsNullOrWhiteSpace(specificationToken) &&
                    string.IsNullOrWhiteSpace(modelToken) &&
                    string.IsNullOrWhiteSpace(degree))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(productCode) &&
                    (string.IsNullOrWhiteSpace(specificationToken) || string.IsNullOrWhiteSpace(modelToken)))
                {
                    invalidRows.Add(rowIndex + 1);
                    continue;
                }

                rows.Add(BuildEntry(productCode, barcode, specificationToken, modelToken, degree));
            }

            if (invalidRows.Count > 0)
            {
                var preview = string.Join("、", invalidRows.Take(5));
                var suffix = invalidRows.Count > 5 ? " 等" : string.Empty;
                throw new InvalidOperationException($"Excel 第 {preview}{suffix} 行缺少有效商品编码，且无法从“周期 + 型号”推导，请修正后再导入。");
            }

            if (rows.Count == 0)
            {
                throw new InvalidOperationException("未识别到可导入的商品编码，请确认表头和数据是否符合标准模板。");
            }

            return rows
                .Where(entry => !string.IsNullOrWhiteSpace(entry.ProductCode))
                .GroupBy(entry => entry.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException("Excel 文件内容无法识别，请确认文件没有损坏，并使用标准 .xlsx 模板重新导入。", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("无法读取 Excel 文件，请先关闭正在占用该文件的 Excel 或 WPS，然后重试。", ex);
        }
    }

    private static ProductCatalogEntry BuildEntry(
        string productCode,
        string barcode,
        string specificationToken,
        string modelToken,
        string degree)
    {
        var normalizedCode = productCode.Trim();
        var normalizedBarcode = barcode.Trim();
        var normalizedSpecificationToken = specificationToken.Trim();
        var normalizedModelToken = modelToken.Trim();
        var normalizedDegree = degree.Trim();

        if (string.IsNullOrWhiteSpace(normalizedCode) &&
            !string.IsNullOrWhiteSpace(normalizedSpecificationToken) &&
            !string.IsNullOrWhiteSpace(normalizedModelToken))
        {
            normalizedCode = $"{normalizedSpecificationToken}{normalizedModelToken}{normalizedDegree}".Trim();
        }

        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            throw new InvalidOperationException("商品编码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(normalizedDegree))
        {
            normalizedDegree = MatchTextHelper.ExtractTrailingDegree(normalizedCode);
        }

        var baseName = MatchTextHelper.RemoveTrailingDegree(normalizedCode).Trim();
        if (string.IsNullOrWhiteSpace(normalizedSpecificationToken) || string.IsNullOrWhiteSpace(normalizedModelToken))
        {
            var inferredSpecificationToken = ExtractSpecificationToken(baseName);
            if (string.IsNullOrWhiteSpace(normalizedSpecificationToken))
            {
                normalizedSpecificationToken = inferredSpecificationToken;
            }

            if (string.IsNullOrWhiteSpace(normalizedModelToken))
            {
                normalizedModelToken = string.IsNullOrWhiteSpace(normalizedSpecificationToken) || normalizedSpecificationToken.Length >= baseName.Length
                    ? baseName
                    : baseName[normalizedSpecificationToken.Length..].Trim();
            }
        }

        var finalBaseName = string.IsNullOrWhiteSpace(normalizedSpecificationToken) && string.IsNullOrWhiteSpace(normalizedModelToken)
            ? baseName
            : $"{normalizedSpecificationToken}{normalizedModelToken}".Trim();

        return new ProductCatalogEntry
        {
            ProductCode = normalizedCode,
            ProductName = normalizedCode,
            SpecCode = string.Empty,
            Barcode = normalizedBarcode,
            BaseName = string.IsNullOrWhiteSpace(finalBaseName) ? normalizedCode : finalBaseName,
            SpecificationToken = normalizedSpecificationToken,
            ModelToken = normalizedModelToken,
            Degree = normalizedDegree,
            SearchText = MatchTextHelper.Compact($"{normalizedCode} {normalizedSpecificationToken} {normalizedModelToken} {normalizedDegree} {normalizedBarcode}"),
            IsOutOfStock = false
        };
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

    private static int FindColumnIndex(IReadOnlyDictionary<int, string> headers, IEnumerable<string> aliases)
    {
        foreach (var header in headers)
        {
            if (aliases.Any(alias => IsHeaderMatch(header.Value, alias)))
            {
                return header.Key;
            }
        }

        return -1;
    }

    private static string GetCellValue(IReadOnlyDictionary<int, string> cells, int columnIndex)
    {
        return columnIndex > 0 && cells.TryGetValue(columnIndex, out var value)
            ? value.Trim()
            : string.Empty;
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

        var dailyMatch = DailySpecificationRegex.Match(baseName);
        if (dailyMatch.Success)
        {
            return dailyMatch.Groups[1].Value.Trim();
        }

        var wearMarkers = new[] { "日抛10片", "日抛2片", "日抛", "半年抛", "年抛", "试戴片", "月抛", "季抛", "双周抛", "周抛" };
        var matchedMarker = wearMarkers
            .Where(marker => baseName.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(marker => marker.Length)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(matchedMarker))
        {
            return matchedMarker;
        }

        var markerIndex = baseName.LastIndexOf("片", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            return baseName[..(markerIndex + 1)];
        }

        markerIndex = baseName.LastIndexOf("抛", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            return baseName[..(markerIndex + 1)];
        }

        return string.Empty;
    }
}
