using System.IO.Compression;
using OrderTextTrainer.Core.Services;
using Xunit;

namespace pc.Tests;

public sealed class ProductCatalogSpecificationParsingTests
{
    [Fact]
    public void Load_ShouldKeepFullDailyPieceCount_WhenProductCodeContainsThirtyPieces()
    {
        var path = CreateWorkbook(new[]
        {
            new[] { "\u5546\u54c1\u7f16\u7801", "\u6761\u7801" },
            new[] { "\u65e5\u629b30\u7247\u661f\u6cb3\u9752375", "TM-030" }
        });

        try
        {
            var reader = new ProductCatalogXlsxReader();
            var entry = Assert.Single(reader.Load(path));

            Assert.Equal("\u65e5\u629b30\u7247", entry.SpecificationToken);
            Assert.Equal("\u661f\u6cb3\u9752", entry.ModelToken);
            Assert.Equal("375", entry.Degree);
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
