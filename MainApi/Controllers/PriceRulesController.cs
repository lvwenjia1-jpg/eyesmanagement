using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/price-rules")]
public sealed class PriceRulesController : ControllerBase
{
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
        var optionMap = await LoadCatalogOptionMapAsync(cancellationToken);
        if (!TryNormalizeRule(
                request.RuleType,
                request.SpecificationToken,
                request.ModelToken,
                request.RequiredQuantity,
                request.PriceValue,
                optionMap,
                out var item,
                out var errorMessage))
        {
            ModelState.AddModelError(nameof(request.RuleType), errorMessage);
            return ValidationProblem(ModelState);
        }

        var existing = await _priceRules.FindByNameAsync(item.PriceName, cancellationToken);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(request.RuleType), "价格规则已存在。");
            return ValidationProblem(ModelState);
        }

        var id = await _priceRules.CreateAsync(item, cancellationToken);
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

        var optionMap = await LoadCatalogOptionMapAsync(cancellationToken);
        if (!TryNormalizeRule(
                request.RuleType,
                request.SpecificationToken,
                request.ModelToken,
                request.RequiredQuantity,
                request.PriceValue,
                optionMap,
                out var item,
                out var errorMessage))
        {
            ModelState.AddModelError(nameof(request.RuleType), errorMessage);
            return ValidationProblem(ModelState);
        }

        var nameConflict = await _priceRules.FindByNameAsync(item.PriceName, cancellationToken);
        if (nameConflict is not null && nameConflict.Id != id)
        {
            ModelState.AddModelError(nameof(request.RuleType), "价格规则已存在。");
            return ValidationProblem(ModelState);
        }

        item.IsActive = request.IsActive;
        await _priceRules.UpdateAsync(id, item, cancellationToken);
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

        var optionMap = await LoadCatalogOptionMapAsync(cancellationToken);
        var validEntries = new List<PriceRuleUpsertItem>();
        var invalidCount = 0;

        foreach (var item in request.Entries)
        {
            if (!TryNormalizeRule(
                    item.RuleType,
                    item.SpecificationToken,
                    item.ModelToken,
                    item.RequiredQuantity,
                    item.PriceValue,
                    optionMap,
                    out var normalizedItem,
                    out _))
            {
                invalidCount += 1;
                continue;
            }

            normalizedItem.IsActive = item.IsActive ?? true;
            validEntries.Add(normalizedItem);
        }

        if (validEntries.Count == 0)
        {
            ModelState.AddModelError(nameof(request.Entries), "导入内容中没有识别到有效的价格规则。");
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
        return new PriceRuleResponse
        {
            Id = record.Id,
            RuleType = record.RuleType,
            PriceName = record.PriceName,
            SpecificationToken = record.SpecificationToken,
            ModelToken = record.ModelToken,
            RequiredQuantity = record.RequiredQuantity,
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
            .Where(option => !string.IsNullOrWhiteSpace(option.SpecificationToken) && !string.IsNullOrWhiteSpace(option.ModelToken))
            .GroupBy(option => BuildCatalogKey(option.SpecificationToken, option.ModelToken), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(option => BuildCatalogKey(option.SpecificationToken, option.ModelToken), StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeRule(
        string? ruleType,
        string? specificationToken,
        string? modelToken,
        int requiredQuantity,
        int priceValue,
        IReadOnlyDictionary<string, ProductCatalogPriceRuleOptionRecord> optionMap,
        out PriceRuleUpsertItem item,
        out string errorMessage)
    {
        var normalizedRuleType = NormalizeRuleType(ruleType);
        var normalizedSpec = NormalizeText(specificationToken);
        var normalizedModel = NormalizeText(modelToken);

        item = new PriceRuleUpsertItem
        {
            RuleType = normalizedRuleType,
            SpecificationToken = normalizedSpec,
            ModelToken = normalizedModel,
            RequiredQuantity = requiredQuantity,
            PriceValue = priceValue,
            IsActive = true
        };

        errorMessage = string.Empty;
        if (!IsKnownRuleType(normalizedRuleType))
        {
            errorMessage = "规则类型无效。";
            return false;
        }

        switch (normalizedRuleType)
        {
            case PriceRuleTypes.Base:
                if (string.IsNullOrWhiteSpace(normalizedSpec))
                {
                    errorMessage = "基础单价必须选择周期。";
                    return false;
                }

                item.ModelToken = string.Empty;
                item.RequiredQuantity = 1;
                item.PriceName = $"单副 / {normalizedSpec}";
                return true;

            case PriceRuleTypes.Bulk:
                if (string.IsNullOrWhiteSpace(normalizedSpec))
                {
                    errorMessage = "多付活动必须选择周期。";
                    return false;
                }

                if (requiredQuantity < 2)
                {
                    errorMessage = "多付活动数量必须大于等于 2。";
                    return false;
                }

                item.ModelToken = string.Empty;
                item.PriceName = $"多付 / {normalizedSpec} / {requiredQuantity}";
                return true;

            case PriceRuleTypes.ClearanceThreshold:
                if (string.IsNullOrWhiteSpace(normalizedSpec))
                {
                    errorMessage = "清仓门槛必须选择周期。";
                    return false;
                }

                if (requiredQuantity < 1)
                {
                    errorMessage = "清仓门槛数量必须大于等于 1。";
                    return false;
                }

                item.ModelToken = string.Empty;
                item.PriceName = $"清仓门槛 / {normalizedSpec} / {requiredQuantity}";
                return true;

            case PriceRuleTypes.Clearance:
                if (string.IsNullOrWhiteSpace(normalizedSpec) || string.IsNullOrWhiteSpace(normalizedModel))
                {
                    errorMessage = "清仓商品必须选择周期和型号。";
                    return false;
                }

                if (!optionMap.ContainsKey(BuildCatalogKey(normalizedSpec, normalizedModel)))
                {
                    errorMessage = "清仓商品必须匹配商品编码目录中的周期和型号。";
                    return false;
                }

                item.RequiredQuantity = 0;
                item.PriceValue = 0;
                item.PriceName = $"清仓 / {normalizedSpec} / {normalizedModel}";
                return true;

            default:
                errorMessage = "规则类型无效。";
                return false;
        }
    }

    private static string BuildCatalogKey(string? specificationToken, string? modelToken)
    {
        return $"{NormalizeText(specificationToken)}||{NormalizeText(modelToken)}";
    }

    private static string NormalizeRuleType(string? value)
    {
        return value?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static bool IsKnownRuleType(string ruleType)
    {
        return ruleType is PriceRuleTypes.Base or PriceRuleTypes.Bulk or PriceRuleTypes.Clearance or PriceRuleTypes.ClearanceThreshold;
    }

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
