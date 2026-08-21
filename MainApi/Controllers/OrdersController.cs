using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using MainApi.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MainApi.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly DashboardOrderRepository _orders;
    private readonly OrderChangeLogRepository _changeLogs;
    private readonly UploadRepository _uploads;
    private readonly HupunTradeTrackingSyncService _trackingSyncService;

    public OrdersController(
        DashboardOrderRepository orders,
        OrderChangeLogRepository changeLogs,
        UploadRepository uploads,
        HupunTradeTrackingSyncService trackingSyncService)
    {
        _orders = orders;
        _changeLogs = changeLogs;
        _uploads = uploads;
        _trackingSyncService = trackingSyncService;
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

        var normalizedReceiverName = request.ReceiverName?.Trim() ?? string.Empty;
        var normalizedAddress = request.ReceiverAddress?.Trim() ?? string.Empty;
        var normalizedMobile = request.ReceiverMobile?.Trim() ?? string.Empty;
        var normalizedTrackingNumber = request.TrackingNumber?.Trim() ?? string.Empty;
        var currentReceiverName = order.ReceiverName?.Trim() ?? string.Empty;
        var currentAddress = order.ReceiverAddress?.Trim() ?? string.Empty;
        var currentMobile = order.ReceiverMobile?.Trim() ?? string.Empty;
        var currentTrackingNumber = order.TrackingNumber?.Trim() ?? string.Empty;
        if (!string.Equals(normalizedTrackingNumber, currentTrackingNumber, StringComparison.Ordinal))
        {
            return BadRequest("快递单号不允许手动编辑。");
        }

        var effectiveReceiverName = string.IsNullOrWhiteSpace(normalizedReceiverName) ? currentReceiverName : normalizedReceiverName;
        var effectiveAddress = string.IsNullOrWhiteSpace(normalizedAddress) ? currentAddress : normalizedAddress;
        var effectiveMobile = string.IsNullOrWhiteSpace(normalizedMobile) ? currentMobile : normalizedMobile;
        var receiverNameChanged = !string.Equals(effectiveReceiverName, currentReceiverName, StringComparison.Ordinal);
        var addressChanged = !string.Equals(effectiveAddress, currentAddress, StringComparison.Ordinal);
        var mobileChanged = !string.Equals(effectiveMobile, currentMobile, StringComparison.Ordinal);
        var amountChanged = request.Amount != order.Amount;
        if (!string.IsNullOrWhiteSpace(currentTrackingNumber) && (receiverNameChanged || addressChanged || mobileChanged || amountChanged))
        {
            return BadRequest("订单已有快递单号，不能再修改收件人、收货地址、订单金额或手机号。");
        }

        if (receiverNameChanged || addressChanged || mobileChanged)
        {
            var upload = await _uploads.FindByBusinessOrderIdAsync(id, cancellationToken);
            if (upload is null)
            {
                return BadRequest("未找到原始上传记录，无法同步收货地址。");
            }

            var tradeInfo = await _trackingSyncService.QueryTradeAsync(order.OrderNo, order.CreatedAtUtc, cancellationToken);
            var modifyRequest = BuildModifyAddressRequest(order, upload, effectiveReceiverName, effectiveAddress, effectiveMobile, tradeInfo);
            if (modifyRequest is null)
            {
                return BadRequest("订单缺少同步万里牛所需的地址或店铺信息。");
            }

            var modifySucceeded = await _trackingSyncService.ModifyTradeAddressAsync(modifyRequest, cancellationToken);
            if (!modifySucceeded)
            {
                return BadRequest("万里牛地址修改失败，订单未保存。");
            }
        }

        await _orders.UpdateOrderFieldsAsync(
            id,
            request.Amount,
            effectiveReceiverName,
            effectiveAddress,
            effectiveMobile,
            currentTrackingNumber,
            cancellationToken);

        if (receiverNameChanged || addressChanged || mobileChanged || amountChanged)
        {
            var modifierLoginName = Request.Headers["X-Dashboard-LoginName"].ToString().Trim();
            var changeSummary = BuildChangeSummary(
                currentReceiverName,
                effectiveReceiverName,
                currentAddress,
                effectiveAddress,
                order.Amount,
                request.Amount,
                currentMobile,
                effectiveMobile);
            await _changeLogs.CreateAsync(order, modifierLoginName, order.Amount, request.Amount, changeSummary, cancellationToken);
        }

        var updated = await _orders.FindByIdAsync(id, cancellationToken);
        return Ok(ToDetailResponse(updated!));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var deleted = await _orders.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
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
            ReceiverMobile = record.ReceiverMobile,
            ReceiverAddress = record.ReceiverAddress,
            Amount = record.Amount,
            TrackingNumber = record.TrackingNumber,
            Status = record.Status,
            IsCancelled = record.IsCancelled,
            HasSpecialPrice = record.HasSpecialPrice,
            SpecialPriceSummary = record.SpecialPriceSummary,
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
            PriceName = item.PriceName,
            UnitPrice = item.UnitPrice,
            LineAmount = item.LineAmount,
            Quantity = item.Quantity
        };
    }

    private static string BuildChangeSummary(
        string previousReceiverName,
        string currentReceiverName,
        string previousAddress,
        string currentAddress,
        decimal previousAmount,
        decimal currentAmount,
        string previousMobile,
        string currentMobile)
    {
        var changes = new List<string>();
        AddChange(changes, "收件人", previousReceiverName, currentReceiverName);
        AddChange(changes, "收货地址", previousAddress, currentAddress);
        if (previousAmount != currentAmount)
        {
            changes.Add($"订单金额：{previousAmount:0.##} -> {currentAmount:0.##}");
        }
        AddChange(changes, "手机号", previousMobile, currentMobile);
        return string.Join("；", changes);
    }

    private static void AddChange(ICollection<string> changes, string fieldName, string previousValue, string currentValue)
    {
        if (!string.Equals(previousValue, currentValue, StringComparison.Ordinal))
        {
            changes.Add($"{fieldName}：{previousValue} -> {currentValue}");
        }
    }

    private static HupunModifyTradeAddressRequest? BuildModifyAddressRequest(
        DashboardOrderDetailRecord order,
        UploadDetailRecord upload,
        string newReceiverName,
        string newReceiverAddress,
        string newReceiverMobile,
        HupunTradeInfo? tradeInfo)
    {
        var billCode = FirstNonEmpty(order.OrderNo, upload.OrderNumber, upload.UploadNo);
        var receiverName = FirstNonEmpty(newReceiverName, tradeInfo?.ReceiverName, order.ReceiverName, upload.ReceiverName);
        var receiverPhone = FirstNonEmpty(
            newReceiverMobile,
            tradeInfo?.Phone,
            upload.ReceiverMobile,
            TryReadNestedString(upload.ExternalRequestJson, "receiver_mobile"),
            TryReadNestedString(upload.SnapshotJson, "receiverMobile"),
            TryReadNestedString(upload.ExternalResponseJson, "receiver_mobile"));
        var shopNick = FirstNonEmpty(
            tradeInfo?.ShopNick,
            TryReadNestedString(upload.ExternalRequestJson, "shop_nick"),
            TryReadNestedString(upload.ExternalResponseJson, "shop_nick"),
            TryReadNestedString(upload.SnapshotJson, "shopNick"));
        var shopType = tradeInfo?.ShopType;
        if (!shopType.HasValue)
        {
            var shopTypeText = FirstNonEmpty(
                TryReadNestedString(upload.ExternalRequestJson, "shop_type"),
                TryReadNestedString(upload.ExternalResponseJson, "shop_type"),
                TryReadNestedString(upload.SnapshotJson, "shopType"));
            if (int.TryParse(shopTypeText, out var parsedShopType))
            {
                shopType = parsedShopType;
            }
        }

        var address = ParseAddress(newReceiverAddress);
        if (tradeInfo is not null &&
            (!string.IsNullOrWhiteSpace(tradeInfo.Province) || !string.IsNullOrWhiteSpace(tradeInfo.City) || !string.IsNullOrWhiteSpace(tradeInfo.Area)))
        {
            address = (
                FirstNonEmpty(tradeInfo.Province, address.Province),
                FirstNonEmpty(tradeInfo.City, address.City),
                FirstNonEmpty(tradeInfo.Area, address.Area),
                ExtractAddressDetail(newReceiverAddress, tradeInfo));
        }

        if (string.IsNullOrWhiteSpace(billCode) ||
            string.IsNullOrWhiteSpace(receiverName) ||
            string.IsNullOrWhiteSpace(receiverPhone) ||
            string.IsNullOrWhiteSpace(shopNick) ||
            !shopType.HasValue ||
            string.IsNullOrWhiteSpace(address.Province) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Area) ||
            string.IsNullOrWhiteSpace(address.Detail))
        {
            return null;
        }

        var postCode = FirstNonEmpty(
            TryReadNestedString(upload.ExternalRequestJson, "post_code"),
            TryReadNestedString(upload.ExternalResponseJson, "post_code"),
            TryReadNestedString(upload.SnapshotJson, "postCode"),
            "000000");

        return new HupunModifyTradeAddressRequest
        {
            BillCode = billCode,
            Name = receiverName,
            Phone = receiverPhone,
            Province = address.Province,
            City = address.City,
            Area = address.Area,
            AddressDetail = address.Detail,
            PostCode = postCode,
            ShopNick = shopNick,
            ShopType = shopType.Value
        };
    }

    private static string ExtractAddressDetail(string newReceiverAddress, HupunTradeInfo? tradeInfo)
    {
        if (tradeInfo is null)
        {
            return ParseAddress(newReceiverAddress).Detail;
        }

        var detail = newReceiverAddress.Trim();
        var prefixes = new[]
        {
            $"{tradeInfo.Province}{tradeInfo.City}{tradeInfo.Area}",
            $"{tradeInfo.Province}{tradeInfo.City}",
            $"{tradeInfo.City}{tradeInfo.Area}",
            tradeInfo.Area
        }.Where(value => !string.IsNullOrWhiteSpace(value));

        foreach (var prefix in prefixes)
        {
            if (detail.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                detail = detail[prefix.Length..].Trim();
                break;
            }
        }

        return string.IsNullOrWhiteSpace(detail) ? ParseAddress(newReceiverAddress).Detail : detail;
    }

    private static (string Province, string City, string Area, string Detail) ParseAddress(string address)
    {
        var normalized = (address ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var provinceMatch = Regex.Match(normalized, @"^(?<province>.*?(?:省|市|自治区|特别行政区))");
        if (!provinceMatch.Success)
        {
            return (string.Empty, string.Empty, string.Empty, normalized);
        }

        var province = provinceMatch.Groups["province"].Value.Trim();
        var remainingAfterProvince = normalized[province.Length..];

        var cityMatch = Regex.Match(remainingAfterProvince, @"^(?<city>.*?(?:市|州|地区|盟))");
        if (!cityMatch.Success && IsMunicipalityProvince(province))
        {
            var municipalityAreaMatch = Regex.Match(remainingAfterProvince, @"^(?<area>.*?(?:区|县|旗|镇|乡|街道))");
            if (!municipalityAreaMatch.Success)
            {
                return (province, province, string.Empty, remainingAfterProvince.Trim());
            }

            var municipalityArea = municipalityAreaMatch.Groups["area"].Value.Trim();
            var municipalityDetail = remainingAfterProvince[municipalityArea.Length..].Trim();
            return (province, province, municipalityArea, municipalityDetail);
        }

        if (!cityMatch.Success)
        {
            return (province, string.Empty, string.Empty, remainingAfterProvince.Trim());
        }

        var city = cityMatch.Groups["city"].Value.Trim();
        var remainingAfterCity = remainingAfterProvince[city.Length..];

        var areaMatch = Regex.Match(remainingAfterCity, @"^(?<area>.*?(?:区|县|旗|镇|乡|街道))");
        if (!areaMatch.Success)
        {
            return (province, city, string.Empty, remainingAfterCity.Trim());
        }

        var area = areaMatch.Groups["area"].Value.Trim();
        var detail = remainingAfterCity[area.Length..].Trim();
        return (province, city, area, detail);
    }

    private static bool IsMunicipalityProvince(string province)
    {
        return string.Equals(province, "北京市", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(province, "上海市", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(province, "天津市", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(province, "重庆市", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string TryReadNestedString(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return TryReadNestedString(document.RootElement, propertyName);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryReadNestedString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString()?.Trim() ?? string.Empty,
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => string.Empty
                    };
                }

                var nested = TryReadNestedString(property.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = TryReadNestedString(item, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return string.Empty;
    }
}
