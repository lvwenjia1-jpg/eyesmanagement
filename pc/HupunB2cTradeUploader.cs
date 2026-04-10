using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WpfApp11;

public sealed class HupunB2cTradeUploader
{
    private static readonly HttpClient HttpClient = new();

    public async Task<HupunUploadAttemptResult> UploadAsync(
        OrderDraft draft,
        UploadConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var businessFields = BuildBusinessFields(draft, configuration);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var systemFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["app_key"] = configuration.AppKey,
            ["format"] = "json",
            ["timestamp"] = timestamp
        };

        var sign = GenerateSign(systemFields, businessFields, configuration.Secret);
        systemFields["sign"] = sign;

        var requestFields = systemFields
            .Concat(businessFields)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        using var content = new FormUrlEncodedContent(requestFields);
        using var response = await HttpClient.PostAsync(configuration.ApiUrl.TrimEnd('/') + "/v1/trades/open", content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        return new HupunUploadAttemptResult(
            draft.DraftId,
            response.IsSuccessStatusCode && IsBusinessSuccess(responseText),
            response.StatusCode,
            responseText,
            requestFields);
    }

    private static Dictionary<string, string> BuildBusinessFields(OrderDraft draft, UploadConfiguration configuration)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["trade_id"] = string.IsNullOrWhiteSpace(draft.OrderNumber) ? draft.DraftId : draft.OrderNumber,
            ["order_id"] = string.IsNullOrWhiteSpace(draft.OrderNumber) ? draft.DraftId : draft.OrderNumber,
            ["buyer_nick"] = draft.ReceiverName,
            ["receiver_name"] = draft.ReceiverName,
            ["receiver_mobile"] = draft.ReceiverMobile,
            ["receiver_address"] = draft.ReceiverAddress,
            ["trade_details"] = JsonSerializer.Serialize(BuildTradeDetails(draft, configuration))
        };

        var addressParts = SplitAddress(draft.ReceiverAddress);
        if (!string.IsNullOrWhiteSpace(addressParts.State))
        {
            fields["receiver_state"] = addressParts.State;
        }

        if (!string.IsNullOrWhiteSpace(addressParts.City))
        {
            fields["receiver_city"] = addressParts.City;
        }

        if (!string.IsNullOrWhiteSpace(addressParts.District))
        {
            fields["receiver_district"] = addressParts.District;
        }

        if (!string.IsNullOrWhiteSpace(draft.Remark))
        {
            fields["seller_memo"] = draft.Remark;
        }

        if (!string.IsNullOrWhiteSpace(configuration.OperatorErpFieldName) && !string.IsNullOrWhiteSpace(draft.OperatorErpId))
        {
            fields[configuration.OperatorErpFieldName] = draft.OperatorErpId;
        }

        if (!string.IsNullOrWhiteSpace(configuration.GiftFieldName))
        {
            fields[configuration.GiftFieldName] = draft.HasGift ? "true" : "false";
        }

        return fields;
    }

    private static List<Dictionary<string, object>> BuildTradeDetails(OrderDraft draft, UploadConfiguration configuration)
    {
        var rows = new List<Dictionary<string, object>>();
        foreach (var item in draft.Items)
        {
            var row = new Dictionary<string, object>
            {
                ["goods_code"] = item.ProductCode,
                ["quantity"] = int.TryParse(item.QuantityText, out var quantity) ? quantity : 1
            };

            if (!string.IsNullOrWhiteSpace(item.ProductName))
            {
                row["goods_name"] = item.ProductName;
            }

            if (!string.IsNullOrWhiteSpace(item.DegreeText))
            {
                row["power"] = item.DegreeText;
            }

            if (item.IsTrial)
            {
                row["is_trial"] = true;
            }

            if (!string.IsNullOrWhiteSpace(configuration.ItemWearPeriodFieldName) && !string.IsNullOrWhiteSpace(item.WearPeriod))
            {
                row[configuration.ItemWearPeriodFieldName] = item.WearPeriod;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string GenerateSign(
        IReadOnlyDictionary<string, string> systemFields,
        IReadOnlyDictionary<string, string> businessFields,
        string secret)
    {
        var builder = new StringBuilder(secret);
        foreach (var pair in systemFields.Concat(businessFields).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key);
            builder.Append(pair.Value);
        }

        builder.Append(secret);
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static bool IsBusinessSuccess(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("success", out var successProperty) &&
                successProperty.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (document.RootElement.TryGetProperty("response", out var responseProperty) &&
                responseProperty.ValueKind != JsonValueKind.Null &&
                responseProperty.ValueKind != JsonValueKind.Undefined)
            {
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static AddressParts SplitAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return AddressParts.Empty;
        }

        var cleaned = Regex.Replace(address, @"\s+", " ").Trim();
        var match = Regex.Match(
            cleaned,
            @"^(?<state>.*?(?:省|自治区|特别行政区|市))?\s*(?<city>.*?(?:市|州|盟|地区))?\s*(?<district>.*?(?:区|县|旗))?\s*(?<detail>.*)$");

        if (!match.Success)
        {
            return new AddressParts(string.Empty, string.Empty, string.Empty, cleaned);
        }

        return new AddressParts(
            match.Groups["state"].Value.Trim(),
            match.Groups["city"].Value.Trim(),
            match.Groups["district"].Value.Trim(),
            match.Groups["detail"].Value.Trim());
    }

    private readonly record struct AddressParts(string State, string City, string District, string Detail)
    {
        public static AddressParts Empty => new(string.Empty, string.Empty, string.Empty, string.Empty);
    }
}

public sealed class HupunUploadAttemptResult
{
    public HupunUploadAttemptResult(
        string draftId,
        bool isSuccess,
        System.Net.HttpStatusCode statusCode,
        string responseText,
        IReadOnlyDictionary<string, string> requestFields)
    {
        DraftId = draftId;
        IsSuccess = isSuccess;
        StatusCode = statusCode;
        ResponseText = responseText;
        RequestFields = requestFields;
    }

    public string DraftId { get; }

    public bool IsSuccess { get; }

    public System.Net.HttpStatusCode StatusCode { get; }

    public string ResponseText { get; }

    public IReadOnlyDictionary<string, string> RequestFields { get; }

    public string DebugText => string.Join(
        Environment.NewLine,
        new[]
        {
            $"draft_id: {DraftId}",
            $"http_status: {(int)StatusCode} {StatusCode}",
            "request:",
            string.Join(Environment.NewLine, RequestFields.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}: {pair.Value}")),
            "response:",
            ResponseText
        });
}
