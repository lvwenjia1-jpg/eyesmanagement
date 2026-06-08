using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/price-rules")]
public sealed class PriceRulesController : ControllerBase
{
    private static readonly char[] ModelTokenSeparators = new[] { ',', '\uFF0C', ';', '\uFF1B', '\u3001', '|', '\r', '\n' };
    private static readonly char[] SpecificationTokenSeparators = new[] { ',', '\uFF0C', ';', '\uFF1B', '\u3001', '|', '\r', '\n' };
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
        var result = await _priceRules.QueryAsync(
            request.Keyword,
            request.IsActive,
            request.PageNumber,
            request.PageSize,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

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
                request.SpecificationTokens,
                request.ModelTokens,
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
                request.SpecificationTokens,
                request.ModelTokens,
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
                    item.SpecificationTokens,
                    item.ModelTokens,
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
        var specificationTokens = SplitSpecificationTokens(record.SpecificationToken);
        var modelTokens = SplitModelTokens(record.ModelToken);
        return new PriceRuleResponse
        {
            Id = record.Id,
            RuleType = record.RuleType,
            PriceName = record.PriceName,
            SpecificationToken = record.SpecificationToken,
            SpecificationTokens = specificationTokens,
            ModelToken = record.ModelToken,
            ModelTokens = modelTokens,
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
        IReadOnlyList<string>? specificationTokens,
        IReadOnlyList<string>? modelTokens,
        string? legacyModelToken,
        int requiredQuantity,
        int priceValue,
        IReadOnlyDictionary<string, ProductCatalogPriceRuleOptionRecord> optionMap,
        out PriceRuleUpsertItem item,
        out string errorMessage)
    {
        var normalizedRuleType = NormalizeRuleType(ruleType);
        var normalizedSpecs = NormalizeSpecificationTokens(specificationTokens, specificationToken);
        var normalizedSpec = JoinSpecificationTokens(normalizedSpecs);
        var normalizedModels = NormalizeModelTokens(modelTokens, legacyModelToken);

        item = new PriceRuleUpsertItem
        {
            RuleType = normalizedRuleType,
            SpecificationToken = normalizedSpec,
            ModelToken = JoinModelTokens(normalizedModels),
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
                if (normalizedSpecs.Count != 1)
                {
                    errorMessage = "单副价必须选择周期。";
                    return false;
                }

                normalizedSpec = normalizedSpecs[0];
                item.SpecificationToken = normalizedSpec;
                item.ModelToken = string.Empty;
                item.RequiredQuantity = 1;
                item.PriceName = $"单副 / {normalizedSpec}";
                return true;

            case PriceRuleTypes.Bulk:
                if (normalizedSpecs.Count != 1)
                {
                    errorMessage = "多付活动必须选择周期。";
                    return false;
                }

                normalizedSpec = normalizedSpecs[0];
                item.SpecificationToken = normalizedSpec;
                if (requiredQuantity < 2)
                {
                    errorMessage = "多付活动数量必须大于等于 2。";
                    return false;
                }

                item.ModelToken = string.Empty;
                item.PriceName = $"多付 / {normalizedSpec} / {requiredQuantity}";
                return true;

            case PriceRuleTypes.Clearance:
                if (normalizedSpecs.Count == 0)
                {
                    errorMessage = "清仓规则必须选择周期。";
                    return false;
                }

                if (normalizedModels.Count == 0)
                {
                    errorMessage = "清仓规则必须至少选择一个型号。";
                    return false;
                }

                if (requiredQuantity < 1)
                {
                    errorMessage = "清仓整包数量必须大于等于 1。";
                    return false;
                }

                if (priceValue < 0)
                {
                    errorMessage = "清仓整包价格不能小于 0。";
                    return false;
                }

                foreach (var normalizedModel in normalizedModels)
                {
                    if (!normalizedSpecs.Any(specification => optionMap.ContainsKey(BuildCatalogKey(specification, normalizedModel))))
                    {
                        errorMessage = $"清仓型号“{normalizedModel}”未在商品编码目录中匹配到所选周期。";
                        return false;
                    }
                }

                item.PriceName = BuildClearancePriceName(normalizedSpecs, normalizedModels, requiredQuantity, priceValue);
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

private static string BuildClearancePriceName(string specificationToken, IReadOnlyList<string> modelTokens, int requiredQuantity, int priceValue)
    {
        return $"清仓 / {specificationToken} / {requiredQuantity}副 / {priceValue}元 / {modelTokens.Count}款";
    }

    private static string BuildClearancePriceName(IReadOnlyList<string> specificationTokens, IReadOnlyList<string> modelTokens, int requiredQuantity, int priceValue)
    {
        var specificationSummary = JoinSpecificationTokens(specificationTokens).Replace("|", "+", StringComparison.OrdinalIgnoreCase);
        return BuildClearancePriceName(specificationSummary, modelTokens, requiredQuantity, priceValue);
    }

    private static string NormalizeRuleType(string? value)
    {
        return value?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static bool IsKnownRuleType(string ruleType)
    {
        return ruleType is PriceRuleTypes.Base or PriceRuleTypes.Bulk or PriceRuleTypes.Clearance;
    }

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static List<string> NormalizeSpecificationTokens(IReadOnlyList<string>? specificationTokens, string? legacySpecificationToken)
    {
        var values = new List<string>();
        if (specificationTokens is not null)
        {
            values.AddRange(specificationTokens);
        }

        if (!string.IsNullOrWhiteSpace(legacySpecificationToken))
        {
            values.AddRange(legacySpecificationToken.Split(SpecificationTokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return values
            .Select(NormalizeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), false))
            .ToList();
    }

    private static List<string> NormalizeModelTokens(IReadOnlyList<string>? modelTokens, string? legacyModelToken)
    {
        var values = new List<string>();
        if (modelTokens is not null)
        {
            values.AddRange(modelTokens);
        }

        if (!string.IsNullOrWhiteSpace(legacyModelToken))
        {
            values.AddRange(legacyModelToken.Split(ModelTokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return values
            .Select(NormalizeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), false))
            .ToList();
    }

    private static List<string> SplitModelTokens(string? value)
    {
        return NormalizeModelTokens(null, value);
    }

    private static List<string> SplitSpecificationTokens(string? value)
    {
        return NormalizeSpecificationTokens(null, value);
    }

    private static string JoinSpecificationTokens(IReadOnlyList<string> specificationTokens)
    {
        return specificationTokens.Count == 0 ? string.Empty : string.Join("|", specificationTokens);
    }

    private static string JoinModelTokens(IReadOnlyList<string> modelTokens)
    {
        return modelTokens.Count == 0 ? string.Empty : string.Join("|", modelTokens);
    }
}
