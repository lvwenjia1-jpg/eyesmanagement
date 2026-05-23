using System.Globalization;
using System.IO;
using System.Text;
using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/exports")]
public sealed class ExportsController : ControllerBase
{
    private const int ExportPageSize = 200;
    private static readonly object ExportFileNameLock = new();
    private static readonly Dictionary<string, int> ExportFileNameCounters = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeZoneInfo BeijingTimeZone = ResolveBeijingTimeZone();
    private readonly BusinessGroupRepository _businessGroups;
    private readonly DashboardOrderRepository _orders;

    public ExportsController(DashboardOrderRepository orders, BusinessGroupRepository businessGroups)
    {
        _orders = orders;
        _businessGroups = businessGroups;
    }

    [HttpGet("orders")]
    public async Task<IActionResult> ExportOrders([FromQuery] ExportOrdersRequest request, CancellationToken cancellationToken)
    {
        if (request.BusinessGroupId <= 0)
        {
            return BadRequest("businessGroupId is required.");
        }

        var query = new DashboardOrderQuery
        {
            BusinessGroupId = request.BusinessGroupId,
            PageNumber = 1,
            PageSize = ExportPageSize,
            StartTimeUtc = request.StartTime,
            EndTimeUtc = request.EndTime,
            OrderNo = request.OrderNo,
            ReceiverName = request.ReceiverName,
            HasTrackingNumber = request.HasTrackingNumber,
            ExcludeCancelledOrders = true,
            SortBy = "createdAtUtc",
            SortDirection = "desc"
        };

        var items = new List<DashboardOrderSummaryRecord>();
        while (true)
        {
            var page = await _orders.QueryByBusinessGroupAsync(query, cancellationToken);
            if (page.Items.Count == 0)
            {
                break;
            }

            items.AddRange(page.Items);
            if (items.Count >= page.TotalCount)
            {
                break;
            }

            query.PageNumber += 1;
        }

        var businessGroup = await _businessGroups.FindByIdAsync(request.BusinessGroupId, cancellationToken);
        var balance = businessGroup?.Balance ?? 0m;
        var businessGroupName = string.IsNullOrWhiteSpace(businessGroup?.Name)
            ? $"业务群{request.BusinessGroupId}"
            : businessGroup!.Name.Trim();

        var csv = BuildOrdersCsv(items, balance);
        var fileName = AllocateUniqueExportFileName(businessGroupName, GetBeijingNow());
        Response.Headers["X-Export-File-Name"] = Uri.EscapeDataString(fileName);
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }

    private static string AllocateUniqueExportFileName(string businessGroupName, DateTime now)
    {
        var safeGroupName = SanitizeFileNamePart(businessGroupName);
        if (string.IsNullOrWhiteSpace(safeGroupName))
        {
            safeGroupName = "业务群";
        }

        var stem = $"{safeGroupName}-{now:yyyyMMddHHmm}";

        lock (ExportFileNameLock)
        {
            var suffix = 0;
            if (ExportFileNameCounters.TryGetValue(stem, out var current))
            {
                suffix = current + 1;
            }

            ExportFileNameCounters[stem] = suffix;
            return suffix == 0 ? $"{stem}.csv" : $"{stem}{suffix}.csv";
        }
    }

    private static string SanitizeFileNamePart(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(ch, '_');
        }

        return text.Trim(' ', '.');
    }

    private static DateTime GetBeijingNow()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BeijingTimeZone);
    }

    private static TimeZoneInfo ResolveBeijingTimeZone()
    {
        foreach (var timeZoneId in new[] { "China Standard Time", "Asia/Shanghai" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Asia/Shanghai",
            TimeSpan.FromHours(8),
            "Beijing Time",
            "Beijing Time");
    }

    private static string BuildOrdersCsv(IReadOnlyList<DashboardOrderSummaryRecord> orders, decimal businessGroupBalance)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("订单号,上传人账号,收件人,电话号码,订单金额,快递单号,结果");

        var totalAmount = 0m;
        foreach (var order in orders)
        {
            totalAmount += order.Amount;
            var resultSummary = string.Join(" ", new[]
            {
                order.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                order.ReceiverName,
                order.TrackingNumber
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            var row = new[]
            {
                order.OrderNo,
                order.UploaderLoginName,
                order.ReceiverName,
                order.ReceiverMobile,
                order.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                order.TrackingNumber,
                resultSummary,
            };
            sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        var remainingBalance = businessGroupBalance - totalAmount;
        sb.AppendLine();
        sb.AppendLine($"业务群余额,{businessGroupBalance.ToString("0.00", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"总金额,{totalAmount.ToString("0.00", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"余额减去总金额,{remainingBalance.ToString("0.00", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"导出订单数,{orders.Count}");

        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Contains('"'))
        {
            text = text.Replace("\"", "\"\"");
        }

        if (text.Contains(',') || text.Contains('\n') || text.Contains('\r') || text.Contains('"'))
        {
            return $"\"{text}\"";
        }

        return text;
    }
}
