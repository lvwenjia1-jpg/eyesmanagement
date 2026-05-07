using MainApi.Contracts;
using MainApi.Data;
using MainApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/wear-period-settings")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class WearPeriodSettingsController : ControllerBase
{
    private readonly WearPeriodSettingsRepository _repository;
    private readonly WearPeriodNormalizationService _normalizationService;

    public WearPeriodSettingsController(
        WearPeriodSettingsRepository repository,
        WearPeriodNormalizationService normalizationService)
    {
        _repository = repository;
        _normalizationService = normalizationService;
    }

    [HttpGet]
    public async Task<ActionResult<WearPeriodSettingsResponse>> Get(CancellationToken cancellationToken)
    {
        var result = await _repository.GetAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateWearPeriodSettingsRequest request, CancellationToken cancellationToken)
    {
        NormalizeRequest(request);
        if (request.WearPeriods.Count == 0)
        {
            ModelState.AddModelError(nameof(request.WearPeriods), "至少保留一个周期。");
            return ValidationProblem(ModelState);
        }

        var knownPeriods = request.WearPeriods
            .Select(item => item.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (request.WearPeriodMappings.Any(item => !knownPeriods.Contains(item.WearPeriod)))
        {
            ModelState.AddModelError(nameof(request.WearPeriodMappings), "周期对照表里存在未定义的目标周期。");
            return ValidationProblem(ModelState);
        }

        await _repository.SaveAsync(request, cancellationToken);
        return NoContent();
    }

    private void NormalizeRequest(UpdateWearPeriodSettingsRequest request)
    {
        request.WearPeriods = request.WearPeriods
            .Select((item, index) => new WearPeriodItemRequest
            {
                Value = _normalizationService.NormalizeWearPeriod(item.Value, new WearPeriodSettingsResponse()),
                SortOrder = item.SortOrder == 0 ? index : item.SortOrder
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        request.WearPeriodMappings = request.WearPeriodMappings
            .Select((item, index) => new WearPeriodAliasItemRequest
            {
                Alias = item.Alias?.Trim() ?? string.Empty,
                WearPeriod = item.WearPeriod?.Trim() ?? string.Empty,
                SortOrder = item.SortOrder == 0 ? index : item.SortOrder
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Alias) && !string.IsNullOrWhiteSpace(item.WearPeriod))
            .GroupBy(item => $"{item.WearPeriod}||{item.Alias}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }
}
