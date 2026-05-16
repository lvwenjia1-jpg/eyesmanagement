using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/business-groups/{businessGroupId:long}/orders")]
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
            EndTimeUtc = request.EndTime,
            SortBy = request.SortBy,
            SortDirection = request.SortDirection
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
            Status = record.Status,
            IsCancelled = record.IsCancelled,
            HasSpecialPrice = record.HasSpecialPrice,
            SpecialPriceSummary = record.SpecialPriceSummary,
            CreatedAtUtc = record.CreatedAtUtc,
            Items = record.Items.Select(item => new DashboardOrderItemResponse
            {
                Id = item.Id,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                PriceName = item.PriceName,
                UnitPrice = item.UnitPrice,
                Quantity = item.Quantity
            }).ToArray()
        };
    }
}
