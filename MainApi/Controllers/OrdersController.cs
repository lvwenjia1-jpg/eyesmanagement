using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly DashboardOrderRepository _orders;

    public OrdersController(DashboardOrderRepository orders)
    {
        _orders = orders;
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<DashboardOrderDetailResponse>> GetById(long id, CancellationToken cancellationToken)
    {
        var order = await _orders.FindByIdAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(ToDetailResponse(order));
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<DashboardOrderDetailResponse>> Update(long id, UpdateDashboardOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await _orders.FindByIdAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        await _orders.UpdateAsync(id, request.Amount, request.TrackingNumber, cancellationToken);
        var updated = await _orders.FindByIdAsync(id, cancellationToken);
        return Ok(ToDetailResponse(updated!));
    }

    private static DashboardOrderDetailResponse ToDetailResponse(DashboardOrderDetailRecord record)
    {
        return new DashboardOrderDetailResponse
        {
            Id = record.Id,
            OrderNo = record.OrderNo,
            BusinessGroupId = record.BusinessGroupId,
            BusinessGroupName = record.BusinessGroupName,
            UploaderLoginName = record.UploaderLoginName,
            ReceiverName = record.ReceiverName,
            ReceiverAddress = record.ReceiverAddress,
            Amount = record.Amount,
            TrackingNumber = record.TrackingNumber,
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
            Items = record.Items.Select(ToItemResponse).ToArray()
        };
    }

    private static DashboardOrderItemResponse ToItemResponse(DashboardOrderItemRecord item)
    {
        return new DashboardOrderItemResponse
        {
            Id = item.Id,
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            Quantity = item.Quantity
        };
    }
}
