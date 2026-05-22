using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using MainApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/business-groups/{businessGroupId:long}/orders")]
public sealed class BusinessGroupOrdersController : ControllerBase
{
    private readonly DashboardOrderRepository _orders;
    private readonly HupunTradeTrackingSyncService _trackingSyncService;

    public BusinessGroupOrdersController(
        DashboardOrderRepository orders,
        HupunTradeTrackingSyncService trackingSyncService)
    {
        _orders = orders;
        _trackingSyncService = trackingSyncService;
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
            OrderNo = request.OrderNo,
            ReceiverName = request.ReceiverName,
            HasTrackingNumber = request.HasTrackingNumber,
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

    [HttpPost("sync-tracking-numbers")]
    public async Task<ActionResult<SyncOrderTrackingNumbersResponse>> SyncTrackingNumbers(
        long businessGroupId,
        [FromBody] SyncOrderTrackingNumbersRequest request,
        CancellationToken cancellationToken)
    {
        var targets = await _orders.ListTrackingSyncTargetsAsync(
            businessGroupId,
            request.StartTime?.ToUniversalTime(),
            request.EndTime?.ToUniversalTime(),
            cancellationToken);

        var updatedCount = 0;
        foreach (var target in targets)
        {
            var expressCode = await _trackingSyncService.QueryExpressCodeAsync(
                target.OrderNumber,
                target.CreatedAtUtc,
                cancellationToken);

            var normalizedTrackingNumber = string.IsNullOrWhiteSpace(expressCode) ? string.Empty : expressCode.Trim();
            if (string.Equals(target.TrackingNumber?.Trim(), normalizedTrackingNumber, StringComparison.Ordinal))
            {
                continue;
            }

            if (await _orders.UpdateTrackingNumberAsync(target.Id, normalizedTrackingNumber, cancellationToken))
            {
                updatedCount++;
            }
        }

        return Ok(new SyncOrderTrackingNumbersResponse
        {
            TotalCount = targets.Count,
            UpdatedCount = updatedCount
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
            ReceiverMobile = record.ReceiverMobile,
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
