using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WpfApp11;

public sealed class HupunB2cTradeUploader
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly SemaphoreSlim RequestGate = new(2, 2);
    internal const int DefaultUploadTradeStatus = 2;
    internal const int CancelUploadTradeStatus = 4;
    private const int DefaultTradeQueryLookbackDays = 7;
    private static readonly DateTime DefaultGoodsQueryStartTime = new(2010, 1, 1, 0, 0, 0);
    private const string DefaultBuyerNick = "system";
    private const int MaxSocketBufferRetries = 3;
    private const int MaxGoodsQueryPages = 10000;
    private const string PreferredOpenApiHost = "open-api.hupun.com";
    private const string LegacyOpenApiHost = "erp-open.hupun.com";
    private const string UploadTradeRelativePath = "/erp/b2c/trades/open";
    private const string TradeListQueryRelativePath = "/erp/opentrade/list/trades";
    private const string GoodsWithSpecListRelativePath = "/erp/goods/spec/open/query/goodswithspeclist";

    public async Task<HupunUploadAttemptResult> UploadAsync(
        OrderDraft draft,
        UploadConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        return await UploadAsync(draft, configuration, DefaultUploadTradeStatus, cancellationToken);
    }

    public async Task<HupunUploadAttemptResult> UploadAsync(
        OrderDraft draft,
        UploadConfiguration configuration,
        int tradeStatus,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(configuration);
        return await UploadWithModeAsync(draft, configuration, TradeWriteMode.OpenTradePush, tradeStatus, cancellationToken);
    }

    public async Task<HupunUploadAttemptResult> QueryTradeListAsync(
        OrderDraft? draft,
        UploadConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(configuration);
        return await QueryTradeListInternalAsync(draft, configuration, cancellationToken);
    }

    public async Task<HupunUploadAttemptResult> QueryGoodsWithSpecListAsync(
        string? specCode,
        string? itemCode,
        string? barCode,
        UploadConfiguration configuration,
        int page = 1,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(configuration);

        var businessFields = BuildGoodsWithSpecListQueryFields(specCode, itemCode, barCode, page, limit);
        return await ExecuteRequestAsync(
            "goodswithspeclist",
            configuration,
            businessFields,
            GoodsQueryMode.GoodsWithSpecListQuery,
            BuildEndpointCandidates(configuration.ApiUrl, GoodsWithSpecListRelativePath, PreferredOpenApiHost),
            "ERP goods query url is invalid. No usable goods query endpoint was found.",
            cancellationToken);
    }

    public async Task<HupunUploadAttemptResult> QueryAllGoodsWithSpecListAsync(
        UploadConfiguration configuration,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(configuration);

        var normalizedLimit = Math.Clamp(limit, 1, 200);
        var endTime = DateTime.Now;
        var mergedGoods = new List<JsonElement>();
        var fetchedPages = 0;
        var page = 1;
        var lastPage = 0;
        string lastRequestUrl = string.Empty;
        string lastFriendlyMessage = string.Empty;
        System.Net.HttpStatusCode lastStatusCode = HttpStatusCode.OK;

        while (page <= MaxGoodsQueryPages)
        {
            var businessFields = BuildGoodsWithSpecListFullQueryFields(DefaultGoodsQueryStartTime, endTime, page, normalizedLimit);
            var pageResult = await ExecuteRequestAsync(
                $"goodswithspeclist-all-{page}",
                configuration,
                businessFields,
                GoodsQueryMode.GoodsWithSpecListQuery,
                BuildEndpointCandidates(configuration.ApiUrl, GoodsWithSpecListRelativePath, PreferredOpenApiHost),
                "ERP goods query url is invalid. No usable goods query endpoint was found.",
                cancellationToken);

            lastRequestUrl = pageResult.RequestUrl;
            lastFriendlyMessage = pageResult.FriendlyMessage;
            lastStatusCode = pageResult.StatusCode;

            if (!pageResult.IsSuccess)
            {
                return pageResult;
            }

            if (!TryReadGoodsQueryItems(pageResult.ResponseText, out var items, out var pagination))
            {
                if (page == 1)
                {
                    return pageResult;
                }

                break;
            }

            if (items.Count == 0)
            {
                break;
            }

            mergedGoods.AddRange(items);
            fetchedPages++;
            lastPage = page;

            if (pagination.TotalPages.HasValue &&
                pagination.CurrentPage.HasValue &&
                pagination.CurrentPage.Value >= pagination.TotalPages.Value)
            {
                break;
            }

            if (items.Count < normalizedLimit)
            {
                break;
            }

            page++;
        }

        var summaryRequestFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["end_time"] = FormatTradeQueryTime(endTime),
            ["fetch_mode"] = "full_catalog_by_modify_time",
            ["limit"] = normalizedLimit.ToString(),
            ["modify_time"] = FormatTradeQueryTime(DefaultGoodsQueryStartTime),
            ["page_end"] = lastPage.ToString(),
            ["page_start"] = "1"
        };

        var summaryResponseText = JsonSerializer.Serialize(new
        {
            code = 0,
            message = "ERP goods query completed.",
            fetched_pages = fetchedPages,
            last_page = lastPage,
            total_count = mergedGoods.Count,
            data = mergedGoods
        });

        return new HupunUploadAttemptResult(
            "goodswithspeclist-all",
            isSuccess: true,
            lastStatusCode,
            summaryResponseText,
            lastRequestUrl,
            summaryRequestFields,
            GoodsQueryMode.GoodsWithSpecListQuery,
            string.IsNullOrWhiteSpace(lastFriendlyMessage)
                ? $"ERP goods query completed. fetched_pages={fetchedPages}, total_count={mergedGoods.Count}."
                : $"{lastFriendlyMessage} fetched_pages={fetchedPages}, total_count={mergedGoods.Count}.");
    }

    private static void ValidateConfiguration(UploadConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.ApiUrl))
        {
            throw new InvalidOperationException("ERP API url is required.");
        }

        if (string.IsNullOrWhiteSpace(configuration.AppKey))
        {
            throw new InvalidOperationException("ERP AppKey is required.");
        }

        if (string.IsNullOrWhiteSpace(configuration.Secret))
        {
            throw new InvalidOperationException("ERP Secret is required.");
        }
    }

    private static async Task<HupunUploadAttemptResult> UploadWithModeAsync(
        OrderDraft draft,
        UploadConfiguration configuration,
        TradeWriteMode mode,
        int tradeStatus,
        CancellationToken cancellationToken)
    {
        var businessFields = BuildTradePushFields(draft, configuration, mode, tradeStatus, DateTime.Now);
        return await ExecuteRequestAsync(
            draft.DraftId,
            configuration,
            businessFields,
            mode,
            BuildUploadEndpointCandidates(configuration.ApiUrl),
            "ERP upload url is invalid. No usable upload endpoint was found.",
            cancellationToken);
    }

    private static async Task<HupunUploadAttemptResult> QueryTradeListInternalAsync(
        OrderDraft? draft,
        UploadConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var businessFields = BuildTradeListQueryFields(draft, configuration, DateTime.Now);
        return await ExecuteRequestAsync(
            draft?.DraftId ?? "open-trade-list",
            configuration,
            businessFields,
            TradeQueryMode.OpenTradeListQuery,
            BuildEndpointCandidates(configuration.ApiUrl, TradeListQueryRelativePath, PreferredOpenApiHost),
            "ERP query url is invalid. No usable trade query endpoint was found.",
            cancellationToken);
    }

    private static async Task<HupunUploadAttemptResult> ExecuteRequestAsync(
        string draftId,
        UploadConfiguration configuration,
        IReadOnlyDictionary<string, string> businessFields,
        Enum requestMode,
        IEnumerable<string> endpointCandidates,
        string invalidEndpointMessage,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var requestFields = BuildRequestFields(configuration, businessFields, requestMode, timestamp);

        HttpResponseMessage? response = null;
        string requestUrl = string.Empty;
        string responseText;
        try
        {
            (response, responseText, requestUrl) = await PostToEndpointAsync(
                endpointCandidates,
                requestFields,
                invalidEndpointMessage,
                cancellationToken);
        }
        catch
        {
            response?.Dispose();
            throw;
        }

        using (response)
        {
            return new HupunUploadAttemptResult(
                draftId,
                response.IsSuccessStatusCode && IsBusinessSuccess(responseText),
                response.StatusCode,
                responseText,
                requestUrl,
                requestFields,
                requestMode,
                BuildFriendlyMessage(configuration, responseText, requestMode));
        }
    }

    private static Dictionary<string, string> BuildTradePushFields(
        OrderDraft draft,
        UploadConfiguration configuration,
        TradeWriteMode mode,
        int tradeStatus,
        DateTime now)
    {
        var addressParts = SplitAddress(draft.ReceiverAddress);
        var tradeId = string.IsNullOrWhiteSpace(draft.OrderNumber) ? draft.DraftId : draft.OrderNumber;
        var receiverAddress = !string.IsNullOrWhiteSpace(addressParts.Detail) ? addressParts.Detail : draft.ReceiverAddress;
        var trade = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["buyer"] = ResolveBuyerNick(draft, configuration),
            ["create_time"] = FormatTradeTime(now),
            ["modify_time"] = FormatTradeTime(now),
            ["orders"] = BuildTradeOrders(draft, mode, tradeId, tradeStatus),
            ["pay_time"] = FormatTradeTime(now.AddHours(-1)),
            ["receiver_address"] = receiverAddress,
            ["receiver_name"] = draft.ReceiverName,
            ["shop_nick"] = ResolveShopNick(configuration),
            ["status"] = tradeStatus,
            ["trade_id"] = tradeId
        };

        if (!string.IsNullOrWhiteSpace(draft.ReceiverMobile))
        {
            trade["receiver_mobile"] = draft.ReceiverMobile;
        }

        if (!string.IsNullOrWhiteSpace(addressParts.State))
        {
            trade["receiver_province"] = addressParts.State;
        }

        if (!string.IsNullOrWhiteSpace(addressParts.City))
        {
            trade["receiver_city"] = addressParts.City;
        }

        if (!string.IsNullOrWhiteSpace(addressParts.District))
        {
            trade["receiver_area"] = addressParts.District;
        }
        else if (!string.IsNullOrWhiteSpace(receiverAddress))
        {
            trade["receiver_area"] = receiverAddress;
        }

        if (!string.IsNullOrWhiteSpace(draft.Remark))
        {
            trade["seller_memo"] = draft.Remark;
        }

        if (!string.IsNullOrWhiteSpace(draft.OperatorErpId))
        {
            trade["sales_mobile"] = draft.OperatorErpId.Trim();
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["trades"] = JsonSerializer.Serialize(new[] { trade })
        };
    }

    private static Dictionary<string, string> BuildTradeListQueryFields(
        OrderDraft? draft,
        UploadConfiguration configuration,
        DateTime now)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["page"] = "1",
            ["limit"] = "20",
            ["create_time"] = FormatTradeQueryTime(now.AddDays(-DefaultTradeQueryLookbackDays)),
            ["end_time"] = FormatTradeQueryTime(now)
        };

        if (!string.IsNullOrWhiteSpace(draft?.OrderNumber))
        {
            fields["bill_code"] = draft.OrderNumber.Trim();
        }

        return fields;
    }

    private static Dictionary<string, string> BuildGoodsWithSpecListQueryFields(
        string? specCode,
        string? itemCode,
        string? barCode,
        int page,
        int limit)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["page"] = Math.Max(1, page).ToString(),
            ["limit"] = Math.Clamp(limit, 1, 200).ToString()
        };

        if (!string.IsNullOrWhiteSpace(specCode))
        {
            fields["spec_code"] = specCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(itemCode))
        {
            fields["item_code"] = itemCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(barCode))
        {
            fields["bar_code"] = barCode.Trim();
        }

        if (!fields.ContainsKey("spec_code") &&
            !fields.ContainsKey("item_code") &&
            !fields.ContainsKey("bar_code"))
        {
            throw new InvalidOperationException("Goods query requires at least one of spec_code, item_code, or bar_code.");
        }

        return fields;
    }

    private static Dictionary<string, string> BuildGoodsWithSpecListFullQueryFields(
        DateTime modifyTime,
        DateTime endTime,
        int page,
        int limit)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["end_time"] = FormatTradeQueryTime(endTime),
            ["limit"] = Math.Clamp(limit, 1, 200).ToString(),
            ["modify_time"] = FormatTradeQueryTime(modifyTime),
            ["page"] = Math.Max(1, page).ToString()
        };
    }

    private static string ResolveBuyerNick(OrderDraft draft, UploadConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(draft.OperatorLoginName))
        {
            return draft.OperatorLoginName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(configuration.ShopNick))
        {
            return configuration.ShopNick.Trim();
        }

        return DefaultBuyerNick;
    }

#if false
    private static string ResolveShopNick(UploadConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.ShopNick))
        {
            return configuration.ShopNick.Trim();
        }

        return "閻磭澧跨拠顓熷瀹搞儱宕?;
    }

#endif
    private static string ResolveShopNick(UploadConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.ShopNick))
        {
            return configuration.ShopNick.Trim();
        }

        return DefaultBuyerNick;
    }

    private static List<Dictionary<string, object>> BuildTradeOrders(
        OrderDraft draft,
        TradeWriteMode mode,
        string tradeId,
        int orderStatus)
    {
        var rows = new List<Dictionary<string, object>>();
        for (var index = 0; index < draft.Items.Count; index++)
        {
            var item = draft.Items[index];
            var goodsCodeSelection = ResolveGoodsCode(item, mode);
            var orderId = $"{tradeId}-{index + 1:000}";

            var row = new Dictionary<string, object>
            {
                ["item_code"] = goodsCodeSelection.Code,
                ["item_id"] = goodsCodeSelection.Code,
                ["item_title"] = goodsCodeSelection.Code,
                ["order_id"] = orderId,
                ["size"] = int.TryParse(item.QuantityText, out var quantity) ? quantity : 1,
                ["status"] = orderStatus
            };

            if (!string.IsNullOrWhiteSpace(item.Remark))
            {
                row["order_attr"] = item.Remark.Trim();
            }

            rows.Add(row);
        }

        return rows;
    }

    private static GoodsCodeSelection ResolveGoodsCode(OrderItemDraft item, TradeWriteMode mode)
    {
        switch (mode)
        {
            case TradeWriteMode.OpenTradePush:
                if (!string.IsNullOrWhiteSpace(item.ProductCode))
                {
                    return new GoodsCodeSelection(item.ProductCode.Trim(), GoodsCodeSource.ProductCode);
                }

                if (!string.IsNullOrWhiteSpace(item.SpecCodeText))
                {
                    return new GoodsCodeSelection(item.SpecCodeText.Trim(), GoodsCodeSource.SpecCode);
                }

                if (!string.IsNullOrWhiteSpace(item.BarcodeText))
                {
                    return new GoodsCodeSelection(item.BarcodeText.Trim(), GoodsCodeSource.Barcode);
                }

                break;
        }

        throw new InvalidOperationException(
            $"Order item '{GetItemDisplayName(item)}' has no ERP code. Please fill product/spec/barcode before upload.");
    }

    private static string GetItemDisplayName(OrderItemDraft item)
    {
        if (!string.IsNullOrWhiteSpace(item.ProductName))
        {
            return item.ProductName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.ProductCode))
        {
            return item.ProductCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.SpecCodeText))
        {
            return item.SpecCodeText.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.BarcodeText))
        {
            return item.BarcodeText.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.SourceText))
        {
            return item.SourceText.Trim();
        }

        return "Unnamed item";
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

    private static string FormatTradeTime(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string NormalizeOpenApiTimestamp(string timestamp)
    {
        if (long.TryParse(timestamp, out var numericTimestamp) && numericTimestamp >= 1000000000000)
        {
            return (numericTimestamp / 1000).ToString();
        }

        return timestamp;
    }

    private static Dictionary<string, string> BuildRequestFields(
        UploadConfiguration configuration,
        IReadOnlyDictionary<string, string> businessFields,
        Enum requestMode,
        string timestamp)
    {
        var openSystemFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_app"] = configuration.AppKey,
            ["_t"] = NormalizeOpenApiTimestamp(timestamp)
        };

        var openSign = GenerateOpenApiSign(openSystemFields, businessFields, configuration.Secret);
        openSystemFields["_sign"] = openSign;

        return openSystemFields
            .Concat(businessFields)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> BuildUploadEndpointCandidates(string apiUrl)
    {
        var candidates = new List<string>();
        foreach (var candidate in BuildEndpointCandidates(apiUrl, UploadTradeRelativePath, PreferredOpenApiHost))
        {
            AddCandidate(candidates, candidate);
        }

        return candidates;
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
            var root = document.RootElement;

            var codeText = string.Empty;
            var hasCode = root.TryGetProperty("code", out var codeProperty) &&
                          TryReadJsonScalar(codeProperty, out codeText);
            if (hasCode && !string.Equals(codeText, "0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (TryReadOpenApiError(root, out _, out _))
            {
                return false;
            }

            if (TryGetDataObject(root, out var dataObject))
            {
                if (TryReadOpenApiError(dataObject, out _, out _))
                {
                    return false;
                }

                if (TryReadBoolean(dataObject, "success", out var nestedSuccess))
                {
                    return nestedSuccess;
                }
            }

            if (TryReadBoolean(root, "success", out var success))
            {
                return success;
            }

            if (root.TryGetProperty("data", out var dataProperty) &&
                dataProperty.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            if (root.TryGetProperty("response", out var responseProperty) &&
                responseProperty.ValueKind != JsonValueKind.Null &&
                responseProperty.ValueKind != JsonValueKind.Undefined)
            {
                return true;
            }

            if (hasCode && string.Equals(codeText, "0", StringComparison.OrdinalIgnoreCase))
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

    private static async Task<(HttpResponseMessage Response, string ResponseText, string RequestUrl)> PostToEndpointAsync(
        IEnumerable<string> endpoints,
        IReadOnlyDictionary<string, string> requestFields,
        string invalidEndpointMessage,
        CancellationToken cancellationToken)
    {
        await RequestGate.WaitAsync(cancellationToken);
        HttpResponseMessage? lastResponse = null;
        string lastResponseText = string.Empty;
        string lastRequestUrl = string.Empty;

        try
        {
            foreach (var endpoint in endpoints)
            {
                lastResponse?.Dispose();
                var response = await PostFormWithRetryAsync(endpoint, requestFields, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsOpenPlatformHtmlPage(response, responseText))
                {
                    response.Dispose();
                    continue;
                }

                if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    return (response, responseText, endpoint);
                }

                lastResponse = response;
                lastResponseText = responseText;
                lastRequestUrl = endpoint;
            }

            if (lastResponse is not null)
            {
                return (lastResponse, lastResponseText, lastRequestUrl);
            }
        }
        finally
        {
            RequestGate.Release();
        }

        throw new InvalidOperationException(invalidEndpointMessage);
    }

    private static async Task<HttpResponseMessage> PostFormWithRetryAsync(
        string endpoint,
        IReadOnlyDictionary<string, string> requestFields,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxSocketBufferRetries; attempt++)
        {
            try
            {
                using var content = new FormUrlEncodedContent(requestFields);
                return await HttpClient.PostAsync(endpoint, content, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsNoBufferSpaceException(ex) && attempt < MaxSocketBufferRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsNoBufferSpaceException(ex))
            {
                throw new InvalidOperationException("ERP request failed after retries because socket buffer space is insufficient.", ex);
            }
        }

        throw new InvalidOperationException("ERP request was not sent successfully.");
    }

    private static bool IsNoBufferSpaceException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SocketException socketException &&
                (socketException.SocketErrorCode == SocketError.NoBufferSpaceAvailable || socketException.NativeErrorCode == 10055))
            {
                return true;
            }
        }

        return false;
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

    private static bool IsOpenPlatformHtmlPage(HttpResponseMessage response, string responseText)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        var trimmed = responseText.TrimStart();
        return trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
               responseText.Contains("window.$context", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> BuildEndpointCandidates(
        string apiUrl,
        string relativePath,
        string? preferredHost = null)
    {
        var configuredUrl = NormalizeUrl(apiUrl);
        var normalizedBaseUrl = NormalizeApiBaseUrl(apiUrl);
        var candidates = new List<string>();
        var preferredHostCandidates = new List<string>();

        if (IsConfiguredForEndpoint(configuredUrl, relativePath))
        {
            AddCandidate(candidates, configuredUrl);
        }

        if (!string.IsNullOrWhiteSpace(normalizedBaseUrl))
        {
            AddCandidate(candidates, normalizedBaseUrl + relativePath);
        }

        if (Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri);
            if (builder.Host.Equals(LegacyOpenApiHost, StringComparison.OrdinalIgnoreCase))
            {
                builder.Host = PreferredOpenApiHost;
                var migratedBaseUrl = builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
                AddCandidate(candidates, migratedBaseUrl + relativePath);
            }
            else if (builder.Host.Equals(PreferredOpenApiHost, StringComparison.OrdinalIgnoreCase))
            {
                builder.Host = LegacyOpenApiHost;
                var migratedBaseUrl = builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
                AddCandidate(candidates, migratedBaseUrl + relativePath);
            }

            if (!string.IsNullOrWhiteSpace(preferredHost))
            {
                builder.Host = preferredHost.Trim();
                var preferredBaseUrl = builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
                AddCandidate(preferredHostCandidates, preferredBaseUrl + relativePath);
            }
        }

        if (preferredHostCandidates.Count > 0)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Contains(preferredHost ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    AddCandidate(preferredHostCandidates, candidate);
                }
            }

            foreach (var candidate in candidates)
            {
                AddCandidate(preferredHostCandidates, candidate);
            }

            return preferredHostCandidates;
        }

        return candidates;
    }

    private static void AddCandidate(ICollection<string> candidates, string candidate)
    {
        if (!candidates.Contains(candidate))
        {
            candidates.Add(candidate);
        }
    }

    private static bool IsConfiguredForEndpoint(string apiUrl, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            return false;
        }

        if (Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri))
        {
            return uri.AbsolutePath.TrimEnd('/').EndsWith(relativePath, StringComparison.OrdinalIgnoreCase);
        }

        return apiUrl.Trim().TrimEnd('/').EndsWith(relativePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeApiBaseUrl(string apiUrl)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(apiUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return StripKnownEndpointSuffix(apiUrl.Trim().TrimEnd('/'));
        }

        var builder = new UriBuilder(uri)
        {
            Path = StripKnownEndpointSuffix(uri.AbsolutePath),
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string NormalizeUrl(string apiUrl)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(apiUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return apiUrl.Trim().TrimEnd('/');
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string StripKnownEndpointSuffix(string path)
    {
        var normalizedPath = path.Trim().TrimEnd('/');
        foreach (var suffix in new[] { UploadTradeRelativePath, TradeListQueryRelativePath, GoodsWithSpecListRelativePath })
        {
            if (normalizedPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var basePath = normalizedPath[..^suffix.Length];
                return string.IsNullOrWhiteSpace(basePath) ? "/" : basePath;
            }
        }

        return string.IsNullOrWhiteSpace(normalizedPath) ? "/" : normalizedPath;
    }

    private static bool TryReadGoodsQueryItems(
        string responseText,
        out List<JsonElement> items,
        out GoodsQueryPagination pagination)
    {
        items = new List<JsonElement>();
        pagination = GoodsQueryPagination.None;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            pagination = ReadGoodsQueryPagination(root);
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in data.EnumerateArray())
            {
                items.Add(item.Clone());
            }

            return true;
        }
        catch
        {
            items.Clear();
            pagination = GoodsQueryPagination.None;
            return false;
        }
    }

    private static GoodsQueryPagination ReadGoodsQueryPagination(JsonElement root)
    {
        var currentPage = ReadJsonInt32(root, "page")
            ?? ReadJsonInt32(root, "page_no")
            ?? ReadJsonInt32(root, "page_index")
            ?? ReadJsonInt32(root, "page_num")
            ?? ReadJsonInt32(root, "current_page")
            ?? ReadJsonInt32(root, "currentPage");
        var totalPages = ReadJsonInt32(root, "total_pages")
            ?? ReadJsonInt32(root, "total_page")
            ?? ReadJsonInt32(root, "pages")
            ?? ReadJsonInt32(root, "page_count")
            ?? ReadJsonInt32(root, "totalPages")
            ?? ReadJsonInt32(root, "totalPage")
            ?? ReadJsonInt32(root, "pageCount");
        return new GoodsQueryPagination(currentPage, totalPages);
    }

    private static int? ReadJsonInt32(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryGetDataObject(JsonElement root, out JsonElement dataObject)
    {
        dataObject = default;
        if (!root.TryGetProperty("data", out var dataProperty) || dataProperty.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        dataObject = dataProperty;
        return true;
    }

    private static bool TryReadBoolean(JsonElement parent, string propertyName, out bool value)
    {
        value = default;
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            bool.TryParse(property.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryReadOpenApiError(JsonElement parent, out string errorCode, out string errorMessage)
    {
        errorCode = parent.TryGetProperty("error_code", out var errorCodeElement) &&
                    errorCodeElement.ValueKind == JsonValueKind.String
            ? errorCodeElement.GetString() ?? string.Empty
            : string.Empty;
        errorMessage = parent.TryGetProperty("error_msg", out var errorMsgElement) &&
                       errorMsgElement.ValueKind == JsonValueKind.String
            ? errorMsgElement.GetString() ?? string.Empty
            : string.Empty;

        return !string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(errorMessage);
    }

#if false
    private static string BuildFriendlyMessage(UploadConfiguration configuration, string responseText, Enum requestMode)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var codeElement) && TryReadJsonScalar(codeElement, out var openCode)
                ? openCode
                : string.Empty;
            var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? string.Empty
                : string.Empty;

            var hasDataObject = TryGetDataObject(root, out var dataObject);
            TryReadOpenApiError(root, out var rootErrorCode, out var rootErrorMessage);
            var errorCode = rootErrorCode;
            var errorMessage = rootErrorMessage;
            if (hasDataObject && string.IsNullOrWhiteSpace(errorCode) && string.IsNullOrWhiteSpace(errorMessage))
            {
                TryReadOpenApiError(dataObject, out errorCode, out errorMessage);
            }

            var hasNestedSuccess = hasDataObject && TryReadBoolean(dataObject, "success", out var nestedSuccess);
            var hasRootSuccess = TryReadBoolean(root, "success", out var rootSuccess);
            var hasExplicitSuccessFlag = hasNestedSuccess || hasRootSuccess;
            var pushSuccess = hasNestedSuccess ? nestedSuccess : (hasRootSuccess ? rootSuccess : false);
            var combinedMessage = string.IsNullOrWhiteSpace(errorMessage) ? message : errorMessage;

            if (requestMode is TradeQueryMode.OpenTradeListQuery)
            {
                if (string.Equals(code, "0", StringComparison.OrdinalIgnoreCase))
                {
                    return "Notice: this query API may not return Taobao/PDD orders, and receiver info can be masked or omitted.";
                }

                if (string.Equals(code, "1005", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(code, "1006", StringComparison.OrdinalIgnoreCase))
                {
                    return "Query requires at least one filter field, such as bill_code/create_time/modify_time/end_time.";
                }
            }

            if (requestMode is GoodsQueryMode.GoodsWithSpecListQuery)
            {
                if (string.Equals(code, "0", StringComparison.OrdinalIgnoreCase))
                {
                    return "ERP goods query completed.";
                }

                if (string.Equals(code, "1005", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(code, "1006", StringComparison.OrdinalIgnoreCase) ||
                    combinedMessage.Contains("spec_code", StringComparison.OrdinalIgnoreCase) ||
                    combinedMessage.Contains("item_code", StringComparison.OrdinalIgnoreCase) ||
                    combinedMessage.Contains("bar_code", StringComparison.OrdinalIgnoreCase))
                {
                    return "Goods query requires at least one of spec_code, item_code, modify_time, or bar_code.";
                }
            }

            if (requestMode is TradeWriteMode.OpenTradePush)
            {
                var hasExplicitFailureFlag = hasExplicitSuccessFlag && !pushSuccess;
                var hasOpenApiError = !string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(errorMessage);
                if ((hasExplicitSuccessFlag && pushSuccess) ||
                    (string.Equals(code, "0", StringComparison.OrdinalIgnoreCase) &&
                     !hasExplicitFailureFlag &&
                     !hasOpenApiError))
                {
                    return "ERP order push succeeded.";
                }

                if (string.Equals(errorCode, "5002", StringComparison.OrdinalIgnoreCase) ||
                    combinedMessage.Contains("shop_nick", StringComparison.OrdinalIgnoreCase) ||
                    combinedMessage.Contains("搴楅摵", StringComparison.OrdinalIgnoreCase))
                {
                    return "ERP push failed: shop info mismatch. Use the shop_nick returned by /erp/opentrade/list/trades.";
                }

                if (string.Equals(code, "1005", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(errorCode, "1005", StringComparison.OrdinalIgnoreCase) ||
                    combinedMessage.Contains("[trades]", StringComparison.OrdinalIgnoreCase))
                {
                    return "ERP push failed: required trade fields are missing.";
                }
            }

            if (string.Equals(errorCode, "5501", StringComparison.OrdinalIgnoreCase) ||
                combinedMessage.Contains("invalid key", StringComparison.OrdinalIgnoreCase))
            {
                return $"ERP AppKey is invalid: {configuration.AppKey}.";
            }

            if (string.Equals(errorCode, "5502", StringComparison.OrdinalIgnoreCase) ||
                combinedMessage.Contains("invalid sign", StringComparison.OrdinalIgnoreCase))
            {
                return "ERP signature is invalid. Please verify _t and signing payload.";
            }

            if (string.Equals(errorCode, "5503", StringComparison.OrdinalIgnoreCase) ||
                combinedMessage.Contains("empty timestamp", StringComparison.OrdinalIgnoreCase))
            {
                return "ERP timestamp is invalid or missing.";
            }

            if (!string.IsNullOrWhiteSpace(combinedMessage))
            {
                return $"ERP request returned: {combinedMessage}";
            }
        }
        catch
        {
        }

        var trimmed = responseText.TrimStart();
        if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return "ERP API returned an HTML page instead of JSON. Check gateway URL.";
        }

        return string.Empty;
    }
#endif

    private static string BuildFriendlyMessage(UploadConfiguration configuration, string responseText, Enum requestMode)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var codeElement) && TryReadJsonScalar(codeElement, out var openCode)
                ? openCode
                : string.Empty;
            var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? string.Empty
                : string.Empty;

            var hasDataObject = TryGetDataObject(root, out var dataObject);
            TryReadOpenApiError(root, out var rootErrorCode, out var rootErrorMessage);
            var errorCode = rootErrorCode;
            var errorMessage = rootErrorMessage;
            if (hasDataObject && string.IsNullOrWhiteSpace(errorCode) && string.IsNullOrWhiteSpace(errorMessage))
            {
                TryReadOpenApiError(dataObject, out errorCode, out errorMessage);
            }

            var nestedSuccess = false;
            var hasNestedSuccess = hasDataObject && TryReadBoolean(dataObject, "success", out nestedSuccess);
            var rootSuccess = false;
            var hasRootSuccess = TryReadBoolean(root, "success", out rootSuccess);
            var hasExplicitSuccessFlag = hasNestedSuccess || hasRootSuccess;
            var pushSuccess = hasNestedSuccess ? nestedSuccess : rootSuccess;
            var combinedMessage = string.IsNullOrWhiteSpace(errorMessage) ? message : errorMessage;

            if (requestMode is TradeQueryMode.OpenTradeListQuery)
            {
                if (string.Equals(code, "0", StringComparison.OrdinalIgnoreCase))
                {
                    return "Notice: this query API may not return Taobao/PDD orders, and receiver info can be masked or omitted.";
                }

                if (string.Equals(code, "1005", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(code, "1006", StringComparison.OrdinalIgnoreCase))
                {
                    return "Query requires at least one filter field, such as bill_code/create_time/modify_time/end_time.";
                }
            }

            if (requestMode is TradeWriteMode.OpenTradePush)
            {
                var hasExplicitFailureFlag = hasExplicitSuccessFlag && !pushSuccess;
                var hasOpenApiError = !string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(errorMessage);
                if ((hasExplicitSuccessFlag && pushSuccess) ||
                    (string.Equals(code, "0", StringComparison.OrdinalIgnoreCase) &&
                     !hasExplicitFailureFlag &&
                     !hasOpenApiError))
                {
                    return "ERP order push succeeded.";
                }

                if (string.Equals(errorCode, "5002", StringComparison.OrdinalIgnoreCase) ||
                    combinedMessage.Contains("shop_nick", StringComparison.OrdinalIgnoreCase) ||
                    combinedMessage.Contains("shop", StringComparison.OrdinalIgnoreCase) ||
                    combinedMessage.Contains("store", StringComparison.OrdinalIgnoreCase))
                {
                    return "ERP push failed: shop info mismatch. Use the shop_nick returned by /erp/opentrade/list/trades.";
                }

                if (string.Equals(code, "1005", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(errorCode, "1005", StringComparison.OrdinalIgnoreCase) ||
                    combinedMessage.Contains("[trades]", StringComparison.OrdinalIgnoreCase))
                {
                    return "ERP push failed: required trade fields are missing.";
                }

                if (combinedMessage.Contains("trade.buyer", StringComparison.OrdinalIgnoreCase) ||
                    combinedMessage.Contains("buyer", StringComparison.OrdinalIgnoreCase) &&
                    combinedMessage.Contains("不能为空", StringComparison.OrdinalIgnoreCase))
                {
                    return "ERP push failed: buyer is required. The upload payload now needs trade.buyer.";
                }
            }

            if (string.Equals(errorCode, "5501", StringComparison.OrdinalIgnoreCase) ||
                combinedMessage.Contains("invalid key", StringComparison.OrdinalIgnoreCase))
            {
                return $"ERP AppKey is invalid: {configuration.AppKey}.";
            }

            if (string.Equals(errorCode, "5502", StringComparison.OrdinalIgnoreCase) ||
                combinedMessage.Contains("invalid sign", StringComparison.OrdinalIgnoreCase))
            {
                return "ERP signature is invalid. Please verify _t and signing payload.";
            }

            if (string.Equals(errorCode, "5503", StringComparison.OrdinalIgnoreCase) ||
                combinedMessage.Contains("empty timestamp", StringComparison.OrdinalIgnoreCase))
            {
                return "ERP timestamp is invalid or missing.";
            }

            if (!string.IsNullOrWhiteSpace(combinedMessage))
            {
                return $"ERP request returned: {combinedMessage}";
            }
        }
        catch
        {
        }

        var trimmed = responseText.TrimStart();
        if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return "ERP API returned an HTML page instead of JSON. Check gateway URL.";
        }

        return string.Empty;
    }

    private static bool TryReadJsonScalar(JsonElement element, out string value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = element.GetString() ?? string.Empty;
                return true;
            case JsonValueKind.Number:
                value = element.GetRawText();
                return true;
            case JsonValueKind.True:
                value = bool.TrueString;
                return true;
            case JsonValueKind.False:
                value = bool.FalseString;
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    private static string FormatTradeQueryTime(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss");
    }

 #if false
    private static AddressParts SplitAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return AddressParts.Empty;
        }

        var cleaned = Regex.Replace(address, @"\s+", " ").Trim();
        var match = Regex.Match(
            cleaned,
            @"^(?<state>(?:閸栨ぞ鍚敮鍊堟径鈺傝Е鐢€堟稉濠冩崳鐢€堥柌宥呯啊鐢€?+?閻簠.+?閼奉亝涓嶉崠绨?+?閻楃懓鍩嗙悰灞炬杺閸?)\s*(?<city>(?:閸栨ぞ鍚敮鍊堟径鈺傝Е鐢€堟稉濠冩崳鐢€堥柌宥呯啊鐢€?+?鐢€?+?瀹哥€?+?閻╃劜.+?閸︽澘灏?)?\s*(?<district>(?:.+?閸栫皸.+?閸樼阜.+?鐢?)?\s*(?<detail>.*)$");

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
#endif

    private static AddressParts SplitAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return AddressParts.Empty;
        }

        var cleaned = Regex.Replace(address, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return AddressParts.Empty;
        }

        const string markerPattern =
            @"^(?<state>.*?(?:省|自治区|特别行政区|市))?(?<city>.*?(?:市|自治州|地区|盟))?(?<district>.*?(?:区|县|旗|市))?(?<detail>.*)$";
        var markerMatch = Regex.Match(cleaned, markerPattern);
        if (markerMatch.Success)
        {
            var state = markerMatch.Groups["state"].Value.Trim();
            var city = markerMatch.Groups["city"].Value.Trim();
            var district = markerMatch.Groups["district"].Value.Trim();
            var detail = markerMatch.Groups["detail"].Value.Trim();

            if (!string.IsNullOrWhiteSpace(state) ||
                !string.IsNullOrWhiteSpace(city) ||
                !string.IsNullOrWhiteSpace(district))
            {
                return new AddressParts(state, city, district, detail);
            }
        }

        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 4)
        {
            return new AddressParts(
                tokens[0],
                tokens[1],
                tokens[2],
                string.Join(' ', tokens, 3, tokens.Length - 3));
        }

        if (tokens.Length == 3)
        {
            return new AddressParts(tokens[0], tokens[1], tokens[2], string.Empty);
        }

        if (tokens.Length == 2)
        {
            return new AddressParts(tokens[0], tokens[1], string.Empty, string.Empty);
        }

        return new AddressParts(string.Empty, string.Empty, string.Empty, cleaned);
    }

    private readonly record struct AddressParts(string State, string City, string District, string Detail)
    {
        public static AddressParts Empty => new(string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private readonly record struct GoodsQueryPagination(int? CurrentPage, int? TotalPages)
    {
        public static GoodsQueryPagination None => new(null, null);
    }

    private enum TradeWriteMode
    {
        OpenTradePush
    }

    private enum TradeQueryMode
    {
        OpenTradeListQuery
    }

    private enum GoodsQueryMode
    {
        GoodsWithSpecListQuery
    }

    private enum GoodsCodeSource
    {
        ProductCode,
        SpecCode,
        Barcode
    }

    private readonly record struct GoodsCodeSelection(string Code, GoodsCodeSource Source)
    {
    }

    internal static IReadOnlyDictionary<string, string> BuildTradeListQueryFieldsForTesting(OrderDraft? draft, UploadConfiguration configuration, DateTime now)
    {
        return BuildTradeListQueryFields(draft, configuration, now);
    }

    internal static IReadOnlyDictionary<string, string> BuildTradePushFieldsForTesting(
        OrderDraft draft,
        UploadConfiguration configuration,
        DateTime now,
        int tradeStatus = DefaultUploadTradeStatus)
    {
        return BuildTradePushFields(draft, configuration, TradeWriteMode.OpenTradePush, tradeStatus, now);
    }

    internal static IReadOnlyDictionary<string, string> BuildGoodsWithSpecListFullQueryFieldsForTesting(DateTime modifyTime, DateTime endTime, int page, int limit)
    {
        return BuildGoodsWithSpecListFullQueryFields(modifyTime, endTime, page, limit);
    }

    internal static IReadOnlyDictionary<string, string> BuildSignedRequestFieldsForTesting(
        UploadConfiguration configuration,
        IReadOnlyDictionary<string, string> businessFields,
        bool queryMode,
        string timestamp)
    {
        return BuildRequestFields(
            configuration,
            businessFields,
            queryMode ? TradeQueryMode.OpenTradeListQuery : TradeWriteMode.OpenTradePush,
            timestamp);
    }

    internal static IReadOnlyList<string> BuildUploadEndpointCandidatesForTesting(string apiUrl)
    {
        return BuildUploadEndpointCandidates(apiUrl);
    }

    internal static bool IsBusinessSuccessForTesting(string responseText)
    {
        return IsBusinessSuccess(responseText);
    }

    internal static string BuildFriendlyMessageForTesting(
        UploadConfiguration configuration,
        string responseText,
        bool queryMode)
    {
        return BuildFriendlyMessage(
            configuration,
            responseText,
            queryMode ? TradeQueryMode.OpenTradeListQuery : TradeWriteMode.OpenTradePush);
    }

    internal static IReadOnlyList<string> BuildEndpointCandidatesForTesting(
        string apiUrl,
        string relativePath,
        string? preferredHost = null)
    {
        return BuildEndpointCandidates(apiUrl, relativePath, preferredHost).ToList();
    }
}

public sealed class HupunUploadAttemptResult
{
    public HupunUploadAttemptResult(
        string draftId,
        bool isSuccess,
        System.Net.HttpStatusCode statusCode,
        string responseText,
        string requestUrl,
        IReadOnlyDictionary<string, string> requestFields,
        Enum requestMode,
        string friendlyMessage)
    {
        DraftId = draftId;
        IsSuccess = isSuccess;
        StatusCode = statusCode;
        ResponseText = responseText;
        RequestUrl = requestUrl;
        RequestFields = requestFields;
        RequestMode = requestMode.ToString();
        FriendlyMessage = friendlyMessage;
    }

    public string DraftId { get; }

    public bool IsSuccess { get; }

    public System.Net.HttpStatusCode StatusCode { get; }

    public string ResponseText { get; }

    public string RequestUrl { get; }

    public IReadOnlyDictionary<string, string> RequestFields { get; }

    public string RequestMode { get; }

    public string FriendlyMessage { get; }

    public string DebugText => string.Join(
        Environment.NewLine,
        (string.IsNullOrWhiteSpace(FriendlyMessage) ? Array.Empty<string>() : new[] { $"message: {FriendlyMessage}" })
        .Concat(new[]
        {
            $"draft_id: {DraftId}",
            $"http_status: {(int)StatusCode} {StatusCode}",
            $"request_url: {RequestUrl}",
            $"client_trade_mode(local): {RequestMode}",
            "request:",
            string.Join(Environment.NewLine, RequestFields.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}: {pair.Value}")),
            "response:",
            ResponseText
        }));
}
