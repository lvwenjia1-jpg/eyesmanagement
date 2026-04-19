using System.Text.Encodings.Web;
using System.Text.Json;
using OrderTextTrainer.Core.Models;

namespace OrderTextTrainer.Core.Services;

public sealed class ProductCatalogRepository
{
    private const string PreferredCatalogXlsxPath = @"C:\Users\W10LL\xwechat_files\wxid_v9fwtqzmr4wp21_e188\msg\file\2026-04\商品信息20260408115031_0(790933011).xlsx";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    private readonly ProductCatalogXlsxReader _xlsxReader = new();

    public string GetDefaultCatalogPath()
    {
        return RuntimeDataPathHelper.GetDataFilePath("product-catalog.json");
    }

    public string GetDefaultOverridePath()
    {
        return RuntimeDataPathHelper.GetDataFilePath("product-matches.json");
    }

    public string GetDefaultWearPeriodOverridePath()
    {
        return RuntimeDataPathHelper.GetDataFilePath("wear-period-overrides.json");
    }

    public string GetPreferredCatalogXlsxPath()
    {
        return PreferredCatalogXlsxPath;
    }

    public IReadOnlyList<ProductCatalogEntry> LoadOrCreateCatalog(string? path = null)
    {
        var fullPath = path ?? GetDefaultCatalogPath();
        if (File.Exists(fullPath))
        {
            var json = File.ReadAllText(fullPath);
            return JsonSerializer.Deserialize<List<ProductCatalogEntry>>(json, JsonOptions) ?? new List<ProductCatalogEntry>();
        }

        if (path is null && File.Exists(PreferredCatalogXlsxPath))
        {
            var catalogFromPreferredXlsx = _xlsxReader.Load(PreferredCatalogXlsxPath);
            if (catalogFromPreferredXlsx.Count > 0)
            {
                SaveCatalog(catalogFromPreferredXlsx, fullPath);
                return catalogFromPreferredXlsx;
            }
        }

        SaveCatalog(Array.Empty<ProductCatalogEntry>(), fullPath);
        return Array.Empty<ProductCatalogEntry>();
    }

    public IReadOnlyList<ProductCatalogEntry> LoadCatalogIfExists(string? path = null)
    {
        var fullPath = path ?? GetDefaultCatalogPath();
        if (!File.Exists(fullPath))
        {
            return Array.Empty<ProductCatalogEntry>();
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            return JsonSerializer.Deserialize<List<ProductCatalogEntry>>(json, JsonOptions) ?? new List<ProductCatalogEntry>();
        }
        catch
        {
            return Array.Empty<ProductCatalogEntry>();
        }
    }

    public IReadOnlyList<ProductCatalogEntry> ImportFromXlsx(string path)
    {
        var catalog = ApplyFileContext(_xlsxReader.Load(path), path);
        SaveCatalog(catalog);
        return catalog;
    }

    public IReadOnlyList<ProductCatalogEntry> LoadFromXlsx(string path)
    {
        return ApplyFileContext(_xlsxReader.Load(path), path);
    }

    public IReadOnlyList<ProductCatalogEntry> ImportFromFiles(IEnumerable<string> paths)
    {
        var entries = MergeCatalogEntries(paths.Select(path =>
            Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? LoadOrCreateCatalog(path)
                : LoadFromXlsx(path)));
        SaveCatalog(entries);
        return entries;
    }

    public void SaveCatalog(IEnumerable<ProductCatalogEntry> catalog, string? path = null)
    {
        var fullPath = path ?? GetDefaultCatalogPath();
        var normalizedCatalog = catalog?.ToList() ?? new List<ProductCatalogEntry>();
        if (!HasMeaningfulEntries(normalizedCatalog) && File.Exists(fullPath))
        {
            var existingCatalog = LoadCatalogIfExists(fullPath);
            if (HasMeaningfulEntries(existingCatalog))
            {
                return;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(normalizedCatalog, JsonOptions);
        File.WriteAllText(fullPath, json);
    }

    private static bool HasMeaningfulEntries(IEnumerable<ProductCatalogEntry> entries)
    {
        return entries.Any(entry =>
            !string.IsNullOrWhiteSpace(entry.ProductCode) ||
            !string.IsNullOrWhiteSpace(entry.ProductName));
    }

    private static IReadOnlyList<ProductCatalogEntry> MergeCatalogEntries(IEnumerable<IReadOnlyList<ProductCatalogEntry>> catalogs)
    {
        return catalogs
            .SelectMany(items => items)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ProductCode))
            .GroupBy(entry => entry.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(CountFilledFields)
                .First())
            .OrderBy(entry => entry.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CountFilledFields(ProductCatalogEntry entry)
    {
        var values = new[]
        {
            entry.ProductCode,
            entry.ProductName,
            entry.SpecCode,
            entry.Barcode,
            entry.BaseName,
            entry.SpecificationToken,
            entry.ModelToken,
            entry.Degree,
            entry.SearchText
        };

        return values.Count(value => !string.IsNullOrWhiteSpace(value));
    }

    private static IReadOnlyList<ProductCatalogEntry> ApplyFileContext(IReadOnlyList<ProductCatalogEntry> entries, string path)
    {
        var inferredWearPeriod = InferWearPeriodFromPath(path);
        if (string.IsNullOrWhiteSpace(inferredWearPeriod))
        {
            return entries;
        }

        return entries
            .Select(entry =>
            {
                if (!string.IsNullOrWhiteSpace(entry.SpecificationToken))
                {
                    return entry;
                }

                return new ProductCatalogEntry
                {
                    ProductCode = entry.ProductCode,
                    ProductName = entry.ProductName,
                    SpecCode = entry.SpecCode,
                    Barcode = entry.Barcode,
                    BaseName = entry.BaseName,
                    SpecificationToken = inferredWearPeriod,
                    ModelToken = string.IsNullOrWhiteSpace(entry.ModelToken) ? entry.BaseName : entry.ModelToken,
                    Degree = entry.Degree,
                    SearchText = MatchTextHelper.Compact($"{entry.ProductCode} {inferredWearPeriod} {entry.ModelToken} {entry.Degree}")
                };
            })
            .ToList();
    }

    private static string InferWearPeriodFromPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        if (fileName.Contains("半年抛", StringComparison.OrdinalIgnoreCase))
        {
            return "半年抛";
        }

        if (fileName.Contains("年抛", StringComparison.OrdinalIgnoreCase))
        {
            return "年抛";
        }

        if (fileName.Contains("日抛10片", StringComparison.OrdinalIgnoreCase))
        {
            return "日抛10片";
        }

        if (fileName.Contains("日抛2片", StringComparison.OrdinalIgnoreCase))
        {
            return "日抛2片";
        }

        if (fileName.Contains("日抛", StringComparison.OrdinalIgnoreCase))
        {
            return "日抛";
        }

        if (fileName.Contains("试戴", StringComparison.OrdinalIgnoreCase))
        {
            return "试戴片";
        }

        return string.Empty;
    }

    public IReadOnlyList<ProductMatchOverride> LoadOverrides(string? path = null)
    {
        var fullPath = path ?? GetDefaultOverridePath();
        if (!File.Exists(fullPath))
        {
            SaveOverrides(Array.Empty<ProductMatchOverride>(), fullPath);
            return Array.Empty<ProductMatchOverride>();
        }

        var json = File.ReadAllText(fullPath);
        return JsonSerializer.Deserialize<List<ProductMatchOverride>>(json, JsonOptions) ?? new List<ProductMatchOverride>();
    }

    public void SaveOverrides(IEnumerable<ProductMatchOverride> overrides, string? path = null)
    {
        var fullPath = path ?? GetDefaultOverridePath();
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(overrides, JsonOptions);
        File.WriteAllText(fullPath, json);
    }

    public IReadOnlyList<WearPeriodOverride> LoadWearPeriodOverrides(string? path = null)
    {
        var fullPath = path ?? GetDefaultWearPeriodOverridePath();
        if (!File.Exists(fullPath))
        {
            SaveWearPeriodOverrides(Array.Empty<WearPeriodOverride>(), fullPath);
            return Array.Empty<WearPeriodOverride>();
        }

        var json = File.ReadAllText(fullPath);
        return JsonSerializer.Deserialize<List<WearPeriodOverride>>(json, JsonOptions) ?? new List<WearPeriodOverride>();
    }

    public void SaveWearPeriodOverrides(IEnumerable<WearPeriodOverride> overrides, string? path = null)
    {
        var fullPath = path ?? GetDefaultWearPeriodOverridePath();
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(overrides, JsonOptions);
        File.WriteAllText(fullPath, json);
    }
}
