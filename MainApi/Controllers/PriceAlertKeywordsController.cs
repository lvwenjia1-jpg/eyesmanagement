using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/price-alert-keywords")]
public sealed class PriceAlertKeywordsController : ControllerBase
{
    private readonly PriceAlertKeywordRepository _keywords;

    public PriceAlertKeywordsController(PriceAlertKeywordRepository keywords)
    {
        _keywords = keywords;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PriceAlertKeywordResponse>>> List(CancellationToken cancellationToken)
    {
        var items = await _keywords.ListAsync(cancellationToken);
        return Ok(items.Select(ToResponse).ToArray());
    }

    [HttpPost]
    public async Task<ActionResult<PriceAlertKeywordResponse>> Create(CreatePriceAlertKeywordRequest request, CancellationToken cancellationToken)
    {
        var normalizedKeyword = request.Keyword.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            ModelState.AddModelError(nameof(request.Keyword), "特殊价格字符不能为空。");
            return ValidationProblem(ModelState);
        }

        var existing = await _keywords.FindByKeywordAsync(normalizedKeyword, cancellationToken);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(request.Keyword), "特殊价格字符已存在。");
            return ValidationProblem(ModelState);
        }

        var id = await _keywords.CreateAsync(normalizedKeyword, cancellationToken);
        var created = await _keywords.FindByIdAsync(id, cancellationToken);
        return Created($"/api/price-alert-keywords/{id}", ToResponse(created!));
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<PriceAlertKeywordResponse>> Update(long id, UpdatePriceAlertKeywordRequest request, CancellationToken cancellationToken)
    {
        var existing = await _keywords.FindByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var normalizedKeyword = request.Keyword.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            ModelState.AddModelError(nameof(request.Keyword), "特殊价格字符不能为空。");
            return ValidationProblem(ModelState);
        }

        var nameConflict = await _keywords.FindByKeywordAsync(normalizedKeyword, cancellationToken);
        if (nameConflict is not null && nameConflict.Id != id)
        {
            ModelState.AddModelError(nameof(request.Keyword), "特殊价格字符已存在。");
            return ValidationProblem(ModelState);
        }

        await _keywords.UpdateAsync(id, normalizedKeyword, request.IsActive, cancellationToken);
        var updated = await _keywords.FindByIdAsync(id, cancellationToken);
        return Ok(ToResponse(updated!));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var existing = await _keywords.FindByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        await _keywords.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private static PriceAlertKeywordResponse ToResponse(PriceAlertKeywordRecord record)
    {
        return new PriceAlertKeywordResponse
        {
            Id = record.Id,
            Keyword = record.Keyword,
            IsActive = record.IsActive,
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc
        };
    }
}
