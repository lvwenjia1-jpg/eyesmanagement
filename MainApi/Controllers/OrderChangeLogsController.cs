using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/order-change-logs")]
public sealed class OrderChangeLogsController : ControllerBase
{
    private const string DashboardLoginHeaderName = "X-Dashboard-LoginName";
    private readonly OrderChangeLogRepository _logs;
    private readonly UserRepository _users;

    public OrderChangeLogsController(OrderChangeLogRepository logs, UserRepository users)
    {
        _logs = logs;
        _users = users;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<OrderChangeLogResponse>>> Query([FromQuery] QueryOrderChangeLogsRequest request, CancellationToken cancellationToken)
    {
        if (!await HasDashboardAccessAsync(cancellationToken)) { return Forbid(); }
        var result = await _logs.QueryAsync(new OrderChangeLogQuery
        {
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            ChangedAtStartUtc = request.ChangedAtStart,
            ChangedAtEndUtc = request.ChangedAtEnd,
            ReceiverName = request.ReceiverName,
            ModifierLoginName = request.ModifierLoginName,
            BusinessGroupName = request.BusinessGroupName,
            OrderNo = request.OrderNo
        }, cancellationToken);
        return Ok(new PagedResponse<OrderChangeLogResponse>
        {
            TotalCount = result.TotalCount, PageNumber = result.PageNumber, PageSize = result.PageSize,
            Items = result.Items.Select(item => new OrderChangeLogResponse
            {
                Id = item.Id, OrderNo = item.OrderNo, BusinessGroupName = item.BusinessGroupName,
                ReceiverName = item.ReceiverName, ModifierLoginName = item.ModifierLoginName,
                ChangedAtUtc = item.ChangedAtUtc, PreviousAmount = item.PreviousAmount,
                CurrentAmount = item.CurrentAmount, AmountDifference = item.AmountDifference,
                ChangeSummary = item.ChangeSummary
            }).ToArray()
        });
    }

    private async Task<bool> HasDashboardAccessAsync(CancellationToken cancellationToken)
    {
        var loginName = Request.Headers[DashboardLoginHeaderName].ToString().Trim();
        var user = string.IsNullOrWhiteSpace(loginName) ? null : await _users.FindByLoginNameAsync(loginName, cancellationToken);
        return user is not null && user.IsActive && UserRoles.CanAccessDashboard(user.Role);
    }
}
