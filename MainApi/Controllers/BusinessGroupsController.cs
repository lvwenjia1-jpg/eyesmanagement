using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/business-groups")]
public sealed class BusinessGroupsController : ControllerBase
{
    private readonly BusinessGroupRepository _businessGroups;

    public BusinessGroupsController(BusinessGroupRepository businessGroups)
    {
        _businessGroups = businessGroups;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<BusinessGroupResponse>>> Query([FromQuery] QueryBusinessGroupsRequest request, CancellationToken cancellationToken)
    {
        var result = await _businessGroups.QueryAsync(request.Keyword, request.PageNumber, request.PageSize, cancellationToken);
        return Ok(new PagedResponse<BusinessGroupResponse>
        {
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            Items = result.Items.Select(ToResponse).ToArray()
        });
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<BusinessGroupResponse>> GetById(long id, CancellationToken cancellationToken)
    {
        var group = await _businessGroups.FindByIdAsync(id, cancellationToken);
        return group is null ? NotFound() : Ok(ToResponse(group));
    }

    [HttpPost]
    public async Task<ActionResult<BusinessGroupResponse>> Create(CreateBusinessGroupRequest request, CancellationToken cancellationToken)
    {
        var normalizedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            ModelState.AddModelError(nameof(request.Name), "业务群名称不能为空。");
            return ValidationProblem(ModelState);
        }

        var existing = await _businessGroups.FindByNameAsync(normalizedName, cancellationToken);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(request.Name), "业务群名称已存在。");
            return ValidationProblem(ModelState);
        }

        var id = await _businessGroups.CreateAsync(normalizedName, request.Balance, cancellationToken);
        var created = await _businessGroups.FindByIdAsync(id, cancellationToken);
        return Created($"/api/business-groups/{id}", ToResponse(created!));
    }

    [HttpPut("{id:long}/balance")]
    public async Task<ActionResult<BusinessGroupResponse>> UpdateBalance(long id, UpdateBusinessGroupBalanceRequest request, CancellationToken cancellationToken)
    {
        var group = await _businessGroups.FindByIdAsync(id, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        await _businessGroups.UpdateBalanceAsync(id, request.Balance, cancellationToken);
        var updated = await _businessGroups.FindByIdAsync(id, cancellationToken);
        return Ok(ToResponse(updated!));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var deleted = await _businessGroups.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private static BusinessGroupResponse ToResponse(BusinessGroupRecord group)
    {
        return new BusinessGroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            Balance = group.Balance,
            OrderCount = group.OrderCount,
            CreatedAtUtc = group.CreatedAtUtc,
            UpdatedAtUtc = group.UpdatedAtUtc
        };
    }
}
