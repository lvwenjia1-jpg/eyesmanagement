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

    public PriceRulesController(PriceRuleRepository priceRules)
    {
        _priceRules = priceRules;
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

    [HttpGet("{id:long}")]
    public async Task<ActionResult<PriceRuleResponse>> GetById(long id, CancellationToken cancellationToken)
    {
        var item = await _priceRules.FindByIdAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(ToResponse(item));
    }

    [HttpPost]
    public async Task<ActionResult<PriceRuleResponse>> Create(CreatePriceRuleRequest request, CancellationToken cancellationToken)
    {
        var normalizedName = request.PriceName.Trim();
        var existing = await _priceRules.FindByNameAsync(normalizedName, cancellationToken);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(request.PriceName), "Price name already exists.");
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

        var normalizedName = request.PriceName.Trim();
        var nameConflict = await _priceRules.FindByNameAsync(normalizedName, cancellationToken);
        if (nameConflict is not null && nameConflict.Id != id)
        {
            ModelState.AddModelError(nameof(request.PriceName), "Price name already exists.");
            return ValidationProblem(ModelState);
        }

        await _priceRules.UpdateAsync(id, normalizedName, request.PriceValue, request.IsActive, cancellationToken);
        var updated = await _priceRules.FindByIdAsync(id, cancellationToken);
        return Ok(ToResponse(updated!));
    }

    [HttpPost("import")]
    public async Task<ActionResult<ImportPriceRulesResponse>> Import(ImportPriceRulesRequest request, CancellationToken cancellationToken)
    {
        if (request.Entries.Count == 0)
        {
            ModelState.AddModelError(nameof(request.Entries), "At least one price rule is required.");
            return ValidationProblem(ModelState);
        }

        var normalizedEntries = request.Entries
            .Select(item => new PriceRuleUpsertItem
            {
                PriceName = item.PriceName.Trim(),
                PriceValue = item.PriceValue,
                IsActive = item.IsActive ?? true
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.PriceName))
            .GroupBy(item => item.PriceName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

        if (normalizedEntries.Length == 0)
        {
            ModelState.AddModelError(nameof(request.Entries), "No valid price rules were found in the import payload.");
            return ValidationProblem(ModelState);
        }

        var result = await _priceRules.UpsertManyAsync(normalizedEntries, cancellationToken);
        return Ok(new ImportPriceRulesResponse
        {
            SourceFileName = request.SourceFileName?.Trim() ?? string.Empty,
            TotalCount = result.TotalCount,
            CreatedCount = result.CreatedCount,
            UpdatedCount = result.UpdatedCount,
            ImportedAtUtc = DateTime.UtcNow
        });
    }

    private static PriceRuleResponse ToResponse(PriceRuleRecord record)
    {
        return new PriceRuleResponse
        {
            Id = record.Id,
            PriceName = record.PriceName,
            PriceValue = record.PriceValue,
            IsActive = record.IsActive,
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc
        };
    }
}
