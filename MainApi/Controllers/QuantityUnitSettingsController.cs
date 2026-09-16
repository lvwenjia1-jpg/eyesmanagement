using MainApi.Contracts;
using MainApi.Data;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/quantity-unit-settings")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class QuantityUnitSettingsController : ControllerBase
{
    private readonly QuantityUnitSettingsRepository _repository;

    public QuantityUnitSettingsController(QuantityUnitSettingsRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<ActionResult<QuantityUnitSettingsResponse>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _repository.GetAsync(cancellationToken));
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateQuantityUnitSettingsRequest request, CancellationToken cancellationToken)
    {
        request.Items = request.Items
            .Select((item, index) => new QuantityUnitSettingItem
            {
                Unit = item.Unit?.Trim() ?? string.Empty,
                ActualQuantity = item.ActualQuantity,
                SortOrder = index
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Unit))
            .GroupBy(item => item.Unit, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (request.Items.Any(item => item.ActualQuantity <= 0))
        {
            ModelState.AddModelError(nameof(request.Items), "实际数量必须是正整数。");
            return ValidationProblem(ModelState);
        }

        await _repository.SaveAsync(request.Items, cancellationToken);
        return NoContent();
    }
}
