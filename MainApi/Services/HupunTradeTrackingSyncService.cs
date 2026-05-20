using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MainApi.Options;
using Microsoft.Extensions.Options;

namespace MainApi.Services;

public sealed class HupunTradeTrackingSyncService
{
    private const int DefaultTradeQueryLookbackDays = 7;
    private const string TradeListQueryRelativePath = "/erp/opentrade/list/trades";
    private const string ModifyTradeAddressRelativePath = "/erp/opentrade/modify/address";
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly HupunOpenApiOptions _options;

    public HupunTradeTrackingSyncService(IOptions<HupunOpenApiOptions> options)
    {
        _options = options.Value;
    }

    public async Task<string> QueryExpressCodeAsync(string orderNumber, DateTime createdAtUtc, CancellationToken cancellationToken = default)
    {
        var normalizedOrderNumber = orderNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedOrderNumber))
        {
            return string.Empty;
        }

        var endTime = DateTime.Now;
        var startTime = endTime.AddDays(-DefaultTradeQueryLookbackDays);

        var requestFields = BuildRequestFields(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bill_code"] = normalizedOrderNumber,
            ["create_time"] = FormatTradeQueryTime(startTime),
            ["end_time"] = FormatTradeQueryTime(endTime),
            ["limit"] = "20",
            ["page"] = "1"
        });

        using var content = new FormUrlEncodedContent(requestFields);
        using var response = await HttpClient.PostAsync(BuildTradeQueryUrl(), content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var trade in dataElement.EnumerateArray())
            {
                var tpTid = ReadString(trade, "tp_tid");
                if (!string.Equals(tpTid, normalizedOrderNumber, StringComparison.Ordinal))
                {
                    continue;
                }

                var expressCode = ReadString(trade, "express_code");
                return string.IsNullOrWhiteSpace(expressCode) ? string.Empty : expressCode.Trim();
            }
        }
        catch
        {
            return string.Empty;
        }

        return string.Empty;
    }

    public async Task<HupunTradeInfo?> QueryTradeAsync(string orderNumber, DateTime? createdAtUtc = null, CancellationToken cancellationToken = default)
    {
        var normalizedOrderNumber = orderNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedOrderNumber))
        {
            return null;
        }

        var endTime = DateTime.Now;
        var startTime = createdAtUtc.HasValue
            ? createdAtUtc.Value.ToLocalTime().AddDays(-1)
            : endTime.AddDays(-DefaultTradeQueryLookbackDays);

        var requestFields = BuildRequestFields(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bill_code"] = normalizedOrderNumber,
            ["create_time"] = FormatTradeQueryTime(startTime),
            ["end_time"] = FormatTradeQueryTime(endTime),
            ["limit"] = "20",
            ["page"] = "1"
        });

        using var content = new FormUrlEncodedContent(requestFields);
        using var response = await HttpClient.PostAsync(BuildTradeQueryUrl(), content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var trade in dataElement.EnumerateArray())
            {
                var tpTid = ReadString(trade, "tp_tid");
                if (!string.Equals(tpTid, normalizedOrderNumber, StringComparison.Ordinal))
                {
                    continue;
                }

                return new HupunTradeInfo
                {
                    BillCode = tpTid,
                    ShopNick = ReadString(trade, "shop_nick"),
                    ShopType = ReadInt32(trade, "shop_type"),
                    ReceiverName = FirstNonEmpty(ReadString(trade, "receiver"), ReadString(trade, "buyer_name")),
                    Phone = ReadString(trade, "phone"),
                    Province = ReadString(trade, "province"),
                    City = ReadString(trade, "city"),
                    Area = FirstNonEmpty(ReadString(trade, "district"), ReadString(trade, "area")),
                    AddressDetail = ReadString(trade, "address"),
                    ExpressCode = ReadString(trade, "express_code")
                };
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public async Task<bool> ModifyTradeAddressAsync(HupunModifyTradeAddressRequest request, CancellationToken cancellationToken = default)
    {
        var requestFields = BuildRequestFields(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bill_code"] = request.BillCode.Trim(),
            ["name"] = request.Name.Trim(),
            ["phone"] = request.Phone.Trim(),
            ["province"] = request.Province.Trim(),
            ["city"] = request.City.Trim(),
            ["area"] = request.Area.Trim(),
            ["address_detail"] = request.AddressDetail.Trim(),
            ["post_code"] = request.PostCode.Trim(),
            ["shop_nick"] = request.ShopNick.Trim(),
            ["shop_type"] = request.ShopType.ToString()
        });

        using var content = new FormUrlEncodedContent(requestFields);
        using var response = await HttpClient.PostAsync(BuildOpenApiUrl(ModifyTradeAddressRelativePath), content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (!root.TryGetProperty("code", out var codeElement))
            {
                return false;
            }

            var code = codeElement.ValueKind == JsonValueKind.Number
                ? codeElement.GetInt32()
                : int.TryParse(codeElement.GetString(), out var parsedCode)
                    ? parsedCode
                    : -1;
            if (code != 0)
            {
                return false;
            }

            if (!root.TryGetProperty("data", out var dataElement))
            {
                return false;
            }

            return dataElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => dataElement.GetInt32() == 1,
                JsonValueKind.String => bool.TryParse(dataElement.GetString(), out var parsed) && parsed,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private Dictionary<string, string> BuildRequestFields(IReadOnlyDictionary<string, string> businessFields)
    {
        var systemFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_app"] = _options.AppKey.Trim(),
            ["_t"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
        };

        systemFields["_sign"] = GenerateOpenApiSign(systemFields, businessFields, _options.Secret.Trim());
        return systemFields
            .Concat(businessFields)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private string BuildTradeQueryUrl()
    {
        return BuildOpenApiUrl(TradeListQueryRelativePath);
    }

    private string BuildOpenApiUrl(string relativePath)
    {
        var rawApiUrl = _options.ApiUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawApiUrl))
        {
            return $"https://open-api.hupun.com/api{relativePath}";
        }

        if (Uri.TryCreate(rawApiUrl, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Host}/api{relativePath}";
        }

        return $"https://open-api.hupun.com/api{relativePath}";
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = 4,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
    }

    private static string GenerateOpenApiSign(
        IReadOnlyDictionary<string, string> systemFields,
        IReadOnlyDictionary<string, string> businessFields,
        string secret)
    {
        var payload = string.Join(
            "&",
            systemFields
                .Concat(businessFields)
                .Where(pair => !string.Equals(pair.Key, "_sign", StringComparison.Ordinal))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={EncodeOpenApiSignValue(pair.Value)}"));

        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{secret}{payload}{secret}"));
        return Convert.ToHexString(bytes);
    }

    private static string EncodeOpenApiSignValue(string value)
    {
        return Uri.EscapeDataString(value ?? string.Empty).Replace("%20", "+", StringComparison.Ordinal);
    }

    private static string FormatTradeQueryTime(DateTime value)
    {
        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}

public sealed class HupunModifyTradeAddressRequest
{
    public string BillCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Province { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Area { get; set; } = string.Empty;

    public string AddressDetail { get; set; } = string.Empty;

    public string PostCode { get; set; } = string.Empty;

    public string ShopNick { get; set; } = string.Empty;

    public int ShopType { get; set; }
}

public sealed class HupunTradeInfo
{
    public string BillCode { get; set; } = string.Empty;

    public string ShopNick { get; set; } = string.Empty;

    public int? ShopType { get; set; }

    public string ReceiverName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Province { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Area { get; set; } = string.Empty;

    public string AddressDetail { get; set; } = string.Empty;

    public string ExpressCode { get; set; } = string.Empty;
}
