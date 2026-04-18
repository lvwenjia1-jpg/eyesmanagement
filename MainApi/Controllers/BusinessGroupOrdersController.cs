using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/business-groups/{businessGroupId:long}/orders")]
[Authorize]
public sealed class BusinessGroupOrdersController : ControllerBase
{
    private readonly DashboardOrderRepository _orders;

    public BusinessGroupOrdersController(DashboardOrderRepository orders)
    {
        _orders = orders;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<DashboardOrderSummaryResponse>>> Query(
        long businessGroupId,
        [FromQuery] QueryBusinessGroupOrdersRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orders.QueryByBusinessGroupAsync(new DashboardOrderQuery
        {
            BusinessGroupId = businessGroupId,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            StartTimeUtc = request.StartTime,
            EndTimeUtc = request.EndTime
        }, cancellationToken);

        return Ok(new PagedResponse<DashboardOrderSummaryResponse>
        {
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            Items = result.Items.Select(ToSummaryResponse).ToArray()
        });
    }

    private static DashboardOrderSummaryResponse ToSummaryResponse(DashboardOrderSummaryRecord record)
    {
        return new DashboardOrderSummaryResponse
        {
            Id = record.Id,
            OrderNo = record.OrderNo,
            UploaderLoginName = record.UploaderLoginName,
            ReceiverName = record.ReceiverName,
            ReceiverAddress = record.ReceiverAddress,
            Amount = record.Amount,
            TrackingNumber = record.TrackingNumber,
            CreatedAtUtc = record.CreatedAtUtc,
            Items = record.Items.Select(item => new DashboardOrderItemResponse
            {
                Id = item.Id,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                Quantity = item.Quantity
            }).ToArray()
        };
    }
}
