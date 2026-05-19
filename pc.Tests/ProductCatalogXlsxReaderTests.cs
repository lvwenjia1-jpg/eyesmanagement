using System.IO.Compression;
using OrderTextTrainer.Core.Services;
using Xunit;

namespace pc.Tests;

public sealed class ProductCatalogXlsxReaderTests
{
    [Fact]
    public void Load_ShouldParseStandardTwoColumnTemplate_AndDefaultOutOfStockToFalse()
    {
        var path = CreateWorkbook(new[]
        {
            new[] { "商品编码", "条码" },
            new[] { "半年抛次元梦境pro紫550", "TM-001" }
        });

        try
        {
            var reader = new ProductCatalogXlsxReader();
            var entries = reader.Load(path);

            var entry = Assert.Single(entries);
            Assert.Equal("半年抛次元梦境pro紫550", entry.ProductCode);
            Assert.Equal("TM-001", entry.Barcode);
            Assert.Equal("半年抛", entry.SpecificationToken);
            Assert.Equal("次元梦境pro紫", entry.ModelToken);
            Assert.Equal("550", entry.Degree);
            Assert.False(entry.IsOutOfStock);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ShouldRejectWorkbookWithoutRequiredHeaders()
    {
        var path = CreateWorkbook(new[]
        {
            new[] { "商品", "条码" },
            new[] { "半年抛次元梦境pro紫550", "TM-001" }
        });

        try
        {
            var reader = new ProductCatalogXlsxReader();
            var ex = Assert.Throws<InvalidOperationException>(() => reader.Load(path));
            Assert.Contains("标准模板", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateWorkbook(string[][] rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml");
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(BuildWorksheetXml(rows));
        return path;
    }

    private static string BuildWorksheetXml(string[][] rows)
    {
        static string ColumnName(int index)
        {
            var name = string.Empty;
            var current = index + 1;
            while (current > 0)
            {
                current--;
                name = (char)('A' + (current % 26)) + name;
                current /= 26;
            }

            return name;
        }

        var body = string.Join(
            string.Empty,
            rows.Select((row, rowIndex) =>
            {
                var cells = string.Join(
                    string.Empty,
                    row.Select((value, columnIndex) =>
                        $"""<c r="{ColumnName(columnIndex)}{rowIndex + 1}" t="inlineStr"><is><t>{System.Security.SecurityElement.Escape(value) ?? string.Empty}</t></is></c>"""));
                return $"""<row r="{rowIndex + 1}">{cells}</row>""";
            }));

        return $$"""
                 <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                 <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                   <sheetData>{{body}}</sheetData>
                 </worksheet>
                 """;
    }
}
