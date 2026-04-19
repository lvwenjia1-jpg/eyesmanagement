using System.Globalization;
using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class UploadsController : ControllerBase
{
    private readonly UploadRepository _uploads;
    private readonly UserRepository _users;
    private readonly BusinessGroupRepository _businessGroups;

    public UploadsController(UploadRepository uploads, UserRepository users, BusinessGroupRepository businessGroups)
    {
        _uploads = uploads;
        _users = users;
        _businessGroups = businessGroups;
    }

    [HttpGet]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<IReadOnlyList<UploadSummaryRecord>>> List([FromQuery] ListUploadsRequest request, CancellationToken cancellationToken)
    {
        var query = ToQuery(request);
        var result = await _uploads.ListAsync(query, cancellationToken);
        Response.Headers["X-Total-Count"] = result.TotalCount.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-Page-Number"] = result.PageNumber.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-Page-Size"] = result.PageSize.ToString(CultureInfo.InvariantCulture);
        return Ok(result.Items);
    }

    [HttpGet("paged")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<UploadListResult>> ListPaged([FromQuery] ListUploadsRequest request, CancellationToken cancellationToken)
    {
        var query = ToQuery(request);
        var result = await _uploads.ListAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("query")]
    public async Task<ActionResult<UploadListResult>> Query([FromQuery] ListUploadsRequest request, CancellationToken cancellationToken)
    {
        var query = ToQuery(request);
        var result = await _uploads.ListAsync(query, cancellationToken);
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
            ModelState.AddModelError(nameof(request.Items), "At least one upload item is required.");
            return ValidationProblem(ModelState);
        }

        var targetLoginName = request.UploaderLoginName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetLoginName))
        {
            ModelState.AddModelError(nameof(request.UploaderLoginName), "UploaderLoginName is required when JWT auth is disabled.");
            return ValidationProblem(ModelState);
        }

        var uploader = await _users.FindByLoginNameAsync(targetLoginName, cancellationToken);
        if (uploader is null || !uploader.IsActive)
        {
            ModelState.AddModelError(nameof(request.UploaderLoginName), "Specified uploader account does not exist or is inactive.");
            return ValidationProblem(ModelState);
        }

        long? businessGroupId = null;
        var businessGroupName = request.BusinessGroupName?.Trim() ?? string.Empty;
        if (request.BusinessGroupId.HasValue)
        {
            var businessGroup = await _businessGroups.FindByIdAsync(request.BusinessGroupId.Value, cancellationToken);
            if (businessGroup is null)
            {
                ModelState.AddModelError(nameof(request.BusinessGroupId), "Specified business group does not exist.");
                return ValidationProblem(ModelState);
            }

            businessGroupId = businessGroup.Id;
            businessGroupName = businessGroup.Name;
        }

        var machineCode = Request.Headers.TryGetValue("X-Machine-Code", out var machineHeader)
            ? (machineHeader.ToString() ?? string.Empty).Trim()
            : string.Empty;

        var command = new UploadCreateCommand
        {
            DraftId = request.DraftId,
            OrderNumber = request.OrderNumber,
            SessionId = request.SessionId,
            BusinessGroupId = businessGroupId,
            BusinessGroupName = businessGroupName,
            UploaderLoginName = uploader.LoginName,
            UploaderDisplayName = uploader.LoginName,
            UploaderErpId = uploader.ErpId,
            UploaderWecomId = string.Empty,
            MachineCode = machineCode,
            ReceiverName = request.ReceiverName,
            ReceiverMobile = request.ReceiverMobile,
            ReceiverAddress = request.ReceiverAddress,
            Remark = request.Remark,
            HasGift = request.HasGift,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Received" : request.Status,
            StatusDetail = request.StatusDetail,
            TrackingNumber = request.TrackingNumber,
            ExternalRequestJson = request.ExternalRequestJson,
            ExternalResponseJson = request.ExternalResponseJson,
            Items = request.Items.Select(item => new UploadItemCommand
            {
                SourceText = item.SourceText,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                PriceName = item.PriceName,
                Quantity = item.Quantity,
                DegreeText = item.DegreeText,
                WearPeriod = item.WearPeriod,
                Remark = item.Remark,
                IsTrial = item.IsTrial
            }).ToList()
        };

        long uploadId;
        try
        {
            uploadId = await _uploads.CreateAsync(command, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(request.Items), ex.Message);
            return ValidationProblem(ModelState);
        }

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
            Status = request.Status,
            UploaderLoginName = request.UploaderLoginName,
            OrderNumber = request.OrderNumber,
            ReceiverKeyword = request.ReceiverKeyword,
            DraftId = request.DraftId
        };
    }

    private static int ToDateKey(DateTime value)
    {
        return int.Parse(value.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
