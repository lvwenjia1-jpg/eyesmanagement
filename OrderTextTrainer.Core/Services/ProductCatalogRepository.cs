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
        return Path.Combine(AppContext.BaseDirectory, "product-catalog.json");
    }

    public string GetDefaultOverridePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "product-matches.json");
    }

    public string GetDefaultWearPeriodOverridePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "wear-period-overrides.json");
    }

    public string GetPreferredCatalogXlsxPath()
    {
        return PreferredCatalogXlsxPath;
    }

    public IReadOnlyList<ProductCatalogEntry> LoadOrCreateCatalog(string? path = null)
    {
        var fullPath = path ?? GetDefaultCatalogPath();
        if (path is null && File.Exists(PreferredCatalogXlsxPath))
        {
            var catalogFromPreferredXlsx = _xlsxReader.Load(PreferredCatalogXlsxPath);
            if (catalogFromPreferredXlsx.Count > 0)
            {
                SaveCatalog(catalogFromPreferredXlsx, fullPath);
                return catalogFromPreferredXlsx;
            }
        }

        if (!File.Exists(fullPath))
        {
            SaveCatalog(Array.Empty<ProductCatalogEntry>(), fullPath);
            return Array.Empty<ProductCatalogEntry>();
        }

        var json = File.ReadAllText(fullPath);
        return JsonSerializer.Deserialize<List<ProductCatalogEntry>>(json, JsonOptions) ?? new List<ProductCatalogEntry>();
    }

    public IReadOnlyList<ProductCatalogEntry> ImportFromXlsx(string path)
    {
        var catalog = _xlsxReader.Load(path);
        SaveCatalog(catalog);
        return catalog;
    }

    public void SaveCatalog(IEnumerable<ProductCatalogEntry> catalog, string? path = null)
    {
        var fullPath = path ?? GetDefaultCatalogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(catalog, JsonOptions);
        File.WriteAllText(fullPath, json);
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
