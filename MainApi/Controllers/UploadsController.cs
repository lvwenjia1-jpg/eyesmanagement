using System.Globalization;
using System.Security.Claims;
using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class UploadsController : ControllerBase
{
    private readonly UploadRepository _uploads;
    private readonly UserRepository _users;

    public UploadsController(UploadRepository uploads, UserRepository users)
    {
        _uploads = uploads;
        _users = users;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UploadSummaryRecord>>> List([FromQuery] ListUploadsRequest request, CancellationToken cancellationToken)
    {
        var result = await _uploads.ListAsync(ToQuery(request), cancellationToken);
        Response.Headers["X-Total-Count"] = result.TotalCount.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-Page-Number"] = result.PageNumber.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-Page-Size"] = result.PageSize.ToString(CultureInfo.InvariantCulture);
        return Ok(result.Items);
    }

    [HttpGet("paged")]
    public async Task<ActionResult<UploadListResult>> ListPaged([FromQuery] ListUploadsRequest request, CancellationToken cancellationToken)
    {
        var result = await _uploads.ListAsync(ToQuery(request), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<UploadDetailRecord>> GetById(long id, CancellationToken cancellationToken)
    {
        var upload = await _uploads.FindByIdAsync(id, cancellationToken);
        return upload is null ? NotFound() : Ok(upload);
    }

    [HttpPost]
    public async Task<ActionResult<UploadDetailRecord>> Create(CreateUploadRequest request, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            ModelState.AddModelError(nameof(request.Items), "至少需要一条商品记录。");
            return ValidationProblem(ModelState);
        }

        var loginName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(loginName))
        {
            return Unauthorized();
        }

        var user = await _users.FindByLoginNameAsync(loginName, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var machineCode = User.FindFirstValue("machine_code") ?? string.Empty;
        var command = new UploadCreateCommand
        {
            DraftId = request.DraftId,
            OrderNumber = request.OrderNumber,
            SessionId = request.SessionId,
            UploaderLoginName = user.LoginName,
            UploaderDisplayName = user.DisplayName,
            UploaderErpId = user.ErpId,
            UploaderWecomId = user.WecomId,
            MachineCode = machineCode,
            ReceiverName = request.ReceiverName,
            ReceiverMobile = request.ReceiverMobile,
            ReceiverAddress = request.ReceiverAddress,
            Remark = request.Remark,
            HasGift = request.HasGift,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "已接收" : request.Status,
            StatusDetail = request.StatusDetail,
            ExternalRequestJson = request.ExternalRequestJson,
            ExternalResponseJson = request.ExternalResponseJson,
            Items = request.Items.Select(item => new UploadItemCommand
            {
                SourceText = item.SourceText,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                DegreeText = item.DegreeText,
                WearPeriod = item.WearPeriod,
                Remark = item.Remark,
                IsTrial = item.IsTrial
            }).ToList()
        };

        var uploadId = await _uploads.CreateAsync(command, cancellationToken);
        var created = await _uploads.FindByIdAsync(uploadId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = uploadId }, created);
    }

    private static UploadListQuery ToQuery(ListUploadsRequest request)
    {
        var exactDate = request.Date?.Date;
        return new UploadListQuery
        {
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            CreatedOn = exactDate.HasValue ? ToDateKey(exactDate.Value) : null,
            CreatedOnFrom = exactDate.HasValue || !request.DateFrom.HasValue ? null : ToDateKey(request.DateFrom.Value.Date),
            CreatedOnTo = exactDate.HasValue || !request.DateTo.HasValue ? null : ToDateKey(request.DateTo.Value.Date),
            MachineCode = request.MachineCode,
            Status = request.Status
        };
    }

    private static int ToDateKey(DateTime value)
    {
        return int.Parse(value.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
