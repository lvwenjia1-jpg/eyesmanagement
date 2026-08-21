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
    private readonly OrderChangeLogRepository _changeLogs;
    private readonly UserRepository _users;

    public BusinessGroupsController(BusinessGroupRepository businessGroups, OrderChangeLogRepository changeLogs, UserRepository users)
    {
        _businessGroups = businessGroups;
        _changeLogs = changeLogs;
        _users = users;
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
        if (request.Balance != group.Balance)
        {
            await _changeLogs.CreateBusinessGroupChangeAsync(
                group,
                "业务群余额修改",
                GetModifierLoginName(),
                group.Balance,
                request.Balance,
                cancellationToken);
        }
        var updated = await _businessGroups.FindByIdAsync(id, cancellationToken);
        return Ok(ToResponse(updated!));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        if (!await IsManagerAsync(cancellationToken))
        {
            return Forbid();
        }

        var group = await _businessGroups.FindByIdAsync(id, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        var deleted = await _businessGroups.DeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound();
        }

        await _changeLogs.CreateBusinessGroupChangeAsync(
            group,
            "业务群删除",
            GetModifierLoginName(),
            group.Balance,
            0m,
            cancellationToken);
        return NoContent();
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

    private string GetModifierLoginName()
    {
        return Request.Headers["X-Dashboard-LoginName"].ToString().Trim();
    }

    private async Task<bool> IsManagerAsync(CancellationToken cancellationToken)
    {
        var loginName = GetModifierLoginName();
        var user = string.IsNullOrWhiteSpace(loginName) ? null : await _users.FindByLoginNameAsync(loginName, cancellationToken);
        return user is not null && user.IsActive && UserRoles.Normalize(user.Role) == UserRoles.Manager;
    }
}
