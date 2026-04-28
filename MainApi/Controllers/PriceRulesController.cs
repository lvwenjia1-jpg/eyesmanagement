using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/price-rules")]
public sealed class PriceRulesController : ControllerBase
{
    private const string PriceNameSeparator = " / ";
    private readonly PriceRuleRepository _priceRules;
    private readonly ProductCatalogRepository _productCatalog;

    public PriceRulesController(PriceRuleRepository priceRules, ProductCatalogRepository productCatalog)
    {
        _priceRules = priceRules;
        _productCatalog = productCatalog;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<PriceRuleResponse>>> Query([FromQuery] QueryPriceRulesRequest request, CancellationToken cancellationToken)
    {
        var result = await _priceRules.QueryAsync(request.Keyword, request.IsActive, request.PageNumber, request.PageSize, cancellationToken);
        return Ok(new PagedResponse<PriceRuleResponse>
        {
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            Items = result.Items.Select(ToResponse).ToArray()
        });
    }

    [HttpGet("catalog-options")]
    public async Task<ActionResult<IReadOnlyList<PriceRuleCatalogOptionResponse>>> GetCatalogOptions(CancellationToken cancellationToken)
    {
        var options = await _productCatalog.ListPriceRuleOptionsAsync(cancellationToken);
        return Ok(options.Select(option => new PriceRuleCatalogOptionResponse
        {
            SpecificationToken = option.SpecificationToken,
            ModelToken = option.ModelToken,
            PriceName = option.PriceName,
            ProductCount = option.ProductCount,
            UpdatedAtUtc = option.UpdatedAtUtc
        }).ToList());
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<PriceRuleResponse>> GetById(long id, CancellationToken cancellationToken)
    {
        var item = await _priceRules.FindByIdAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(ToResponse(item));
    }

    [HttpPost]
    public async Task<ActionResult<PriceRuleResponse>> Create(CreatePriceRuleRequest request, CancellationToken cancellationToken)
    {
        var catalogOptionMap = await LoadCatalogOptionMapAsync(cancellationToken);
        if (!TryResolveCatalogOption(
                request.PriceName,
                request.SpecificationToken,
                request.ModelToken,
                catalogOptionMap,
                out var catalogOption))
        {
            ModelState.AddModelError(nameof(request.PriceName), "价格规则必须匹配商品编码目录中的“周期 + 型号”组合。");
            return ValidationProblem(ModelState);
        }

        var normalizedName = catalogOption.PriceName;
        var existing = await _priceRules.FindByNameAsync(normalizedName, cancellationToken);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(request.PriceName), "价格名称已存在。");
            return ValidationProblem(ModelState);
        }

        var id = await _priceRules.CreateAsync(normalizedName, request.PriceValue, cancellationToken);
        var created = await _priceRules.FindByIdAsync(id, cancellationToken);
        return Created($"/api/price-rules/{id}", ToResponse(created!));
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<PriceRuleResponse>> Update(long id, UpdatePriceRuleRequest request, CancellationToken cancellationToken)
    {
        var existing = await _priceRules.FindByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var catalogOptionMap = await LoadCatalogOptionMapAsync(cancellationToken);
        if (!TryResolveCatalogOption(
                request.PriceName,
                request.SpecificationToken,
                request.ModelToken,
                catalogOptionMap,
                out var catalogOption))
        {
            ModelState.AddModelError(nameof(request.PriceName), "价格规则必须匹配商品编码目录中的“周期 + 型号”组合。");
            return ValidationProblem(ModelState);
        }

        var normalizedName = catalogOption.PriceName;
        var nameConflict = await _priceRules.FindByNameAsync(normalizedName, cancellationToken);
        if (nameConflict is not null && nameConflict.Id != id)
        {
            ModelState.AddModelError(nameof(request.PriceName), "价格名称已存在。");
            return ValidationProblem(ModelState);
        }

        await _priceRules.UpdateAsync(id, normalizedName, request.PriceValue, request.IsActive, cancellationToken);
        var updated = await _priceRules.FindByIdAsync(id, cancellationToken);
        return Ok(ToResponse(updated!));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var existing = await _priceRules.FindByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        await _priceRules.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("import")]
    public async Task<ActionResult<ImportPriceRulesResponse>> Import(ImportPriceRulesRequest request, CancellationToken cancellationToken)
    {
        if (request.Entries.Count == 0)
        {
            ModelState.AddModelError(nameof(request.Entries), "至少需要一条价格规则。");
            return ValidationProblem(ModelState);
        }

        var catalogOptionMap = await LoadCatalogOptionMapAsync(cancellationToken);
        var validEntries = new List<PriceRuleUpsertItem>();
        var invalidCount = 0;

        foreach (var item in request.Entries)
        {
            if (!TryResolveCatalogOption(
                    item.PriceName,
                    item.SpecificationToken,
                    item.ModelToken,
                    catalogOptionMap,
                    out var catalogOption))
            {
                invalidCount += 1;
                continue;
            }

            validEntries.Add(new PriceRuleUpsertItem
            {
                PriceName = catalogOption.PriceName,
                PriceValue = item.PriceValue,
                IsActive = item.IsActive ?? true
            });
        }

        if (validEntries.Count == 0)
        {
            ModelState.AddModelError(nameof(request.Entries), "导入内容中没有识别到可匹配商品目录的价格规则。");
            return ValidationProblem(ModelState);
        }

        var normalizedEntries = validEntries
            .GroupBy(item => item.PriceName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

        var deduplicatedCount = validEntries.Count - normalizedEntries.Length;
        var skippedCount = invalidCount + deduplicatedCount;

        var result = await _priceRules.UpsertManyAsync(normalizedEntries, cancellationToken);
        return Ok(new ImportPriceRulesResponse
        {
            SourceFileName = request.SourceFileName?.Trim() ?? string.Empty,
            TotalCount = request.Entries.Count,
            CreatedCount = result.CreatedCount,
            UpdatedCount = result.UpdatedCount,
            SkippedCount = skippedCount,
            ImportedAtUtc = DateTime.UtcNow
        });
    }

    private static PriceRuleResponse ToResponse(PriceRuleRecord record)
    {
        var (specificationToken, modelToken) = SplitPriceName(record.PriceName);
        return new PriceRuleResponse
        {
            Id = record.Id,
            PriceName = record.PriceName,
            SpecificationToken = specificationToken,
            ModelToken = modelToken,
            PriceValue = record.PriceValue,
            IsActive = record.IsActive,
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc
        };
    }

    private async Task<Dictionary<string, ProductCatalogPriceRuleOptionRecord>> LoadCatalogOptionMapAsync(CancellationToken cancellationToken)
    {
        var options = await _productCatalog.ListPriceRuleOptionsAsync(cancellationToken);
        return options
            .Where(option => !string.IsNullOrWhiteSpace(option.PriceName))
            .GroupBy(option => option.PriceName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(option => option.PriceName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryResolveCatalogOption(
        string? priceName,
        string? specificationToken,
        string? modelToken,
        IReadOnlyDictionary<string, ProductCatalogPriceRuleOptionRecord> catalogOptionMap,
        out ProductCatalogPriceRuleOptionRecord catalogOption)
    {
        var normalizedSpecificationToken = NormalizeText(specificationToken);
        var normalizedModelToken = NormalizeText(modelToken);
        var normalizedPriceName = NormalizePriceName(priceName);

        if (!string.IsNullOrWhiteSpace(normalizedSpecificationToken) || !string.IsNullOrWhiteSpace(normalizedModelToken))
        {
            if (string.IsNullOrWhiteSpace(normalizedSpecificationToken) || string.IsNullOrWhiteSpace(normalizedModelToken))
            {
                catalogOption = new ProductCatalogPriceRuleOptionRecord();
                return false;
            }

            normalizedPriceName = ComposePriceName(normalizedSpecificationToken, normalizedModelToken);
        }

        if (string.IsNullOrWhiteSpace(normalizedPriceName))
        {
            catalogOption = new ProductCatalogPriceRuleOptionRecord();
            return false;
        }

        return catalogOptionMap.TryGetValue(normalizedPriceName, out catalogOption!);
    }

    private static (string SpecificationToken, string ModelToken) SplitPriceName(string? priceName)
    {
        var normalized = NormalizeText(priceName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return (string.Empty, string.Empty);
        }

        var parts = normalized.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            return (parts[0], parts[1]);
        }

        return (string.Empty, normalized);
    }

    private static string NormalizePriceName(string? priceName)
    {
        var (specificationToken, modelToken) = SplitPriceName(priceName);
        if (!string.IsNullOrWhiteSpace(specificationToken) || !string.IsNullOrWhiteSpace(modelToken))
        {
            return ComposePriceName(specificationToken, modelToken);
        }

        return string.Empty;
    }

    private static string ComposePriceName(string? specificationToken, string? modelToken)
    {
        var normalizedSpecificationToken = NormalizeText(specificationToken);
        var normalizedModelToken = NormalizeText(modelToken);
        if (string.IsNullOrWhiteSpace(normalizedSpecificationToken))
        {
            return normalizedModelToken;
        }

        if (string.IsNullOrWhiteSpace(normalizedModelToken))
        {
            return normalizedSpecificationToken;
        }

        return $"{normalizedSpecificationToken}{PriceNameSeparator}{normalizedModelToken}";
    }

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
