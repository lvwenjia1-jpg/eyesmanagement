using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderTextTrainer.Core.Models;

namespace WpfApp11;

public sealed class MainApiSyncClient
{
    private static readonly HttpClient HttpClient = new();
    private static readonly TimeSpan SessionCacheDurationWhenTokenMissing = TimeSpan.FromMinutes(10);

    private string _token = string.Empty;
    private DateTime _sessionExpiresAtUtc = DateTime.MinValue;
    private string _cacheKey = string.Empty;
    private MainApiLoginUser _cachedUser = new();

    public async Task<MainApiLoginUser> ValidateLoginAsync(MainApiConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await EnsureSessionAsync(configuration, cancellationToken);
        return CloneUser(_cachedUser);
    }

    public async Task<MachineCodeValidationResult> ValidateMachineCodeAsync(
        MainApiConfiguration configuration,
        string machineCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedMachineCode = machineCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedMachineCode))
        {
            return new MachineCodeValidationResult
            {
                Code = string.Empty,
                Exists = false,
                IsActive = false
            };
        }

        var query = new Dictionary<string, string>
        {
            ["keyword"] = normalizedMachineCode,
            ["pageNumber"] = "1",
            ["pageSize"] = "200"
        };
        var queryString = string.Join("&", query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var requestPath = string.IsNullOrWhiteSpace(queryString)
            ? "/api/machines"
            : $"/api/machines?{queryString}";

        using var response = await HttpClient.GetAsync(
            BuildUri(configuration.BaseUrl, requestPath),
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<PagedMachineResponse>(cancellationToken: cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException("机器码校验接口返回为空。");
        }

        var exact = payload.Items
            .FirstOrDefault(item => string.Equals(item.Code, normalizedMachineCode, StringComparison.OrdinalIgnoreCase));
        if (exact is null)
        {
            return new MachineCodeValidationResult
            {
                Code = normalizedMachineCode,
                Exists = false,
                IsActive = false
            };
        }

        return new MachineCodeValidationResult
        {
            Code = exact.Code,
            Exists = true,
            IsActive = exact.IsActive,
            Id = exact.Id,
            Description = exact.Description
        };
    }

    public async Task<UploadQueryResult> QueryUploadsByAccountAsync(
        MainApiConfiguration configuration,
        string uploaderLoginName,
        int pageNumber = 1,
        int pageSize = 50,
        string draftId = "",
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>
        {
            ["pageNumber"] = Math.Max(1, pageNumber).ToString(),
            ["pageSize"] = Math.Clamp(pageSize, 1, 500).ToString(),
            ["uploaderLoginName"] = uploaderLoginName?.Trim() ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(draftId))
        {
            query["draftId"] = draftId.Trim();
        }

        var queryString = string.Join("&", query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var requestPath = string.IsNullOrWhiteSpace(queryString)
            ? "/api/uploads/query"
            : $"/api/uploads/query?{queryString}";

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(configuration.BaseUrl, requestPath));
        await AuthorizeAsync(request, configuration, cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<UploadQueryResult>(cancellationToken: cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException("订单查询接口返回为空。");
        }

        return payload;
    }

    public async Task<UploadDetailResult> GetUploadByIdAsync(
        MainApiConfiguration configuration,
        long uploadId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(configuration.BaseUrl, $"/api/uploads/{uploadId}"));
        await AuthorizeAsync(request, configuration, cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<UploadDetailResult>(cancellationToken: cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException("上传记录详情接口返回为空。");
        }

        return payload;
    }

    public async Task<IReadOnlyList<BusinessGroupSummary>> QueryBusinessGroupsAsync(
        MainApiConfiguration configuration,
        string keyword = "",
        int pageNumber = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>
        {
            ["pageNumber"] = Math.Max(1, pageNumber).ToString(),
            ["pageSize"] = Math.Clamp(pageSize, 1, 500).ToString(),
            ["keyword"] = keyword?.Trim() ?? string.Empty
        };

        var queryString = string.Join("&", query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var requestPath = string.IsNullOrWhiteSpace(queryString)
            ? "/api/business-groups"
            : $"/api/business-groups?{queryString}";

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(configuration.BaseUrl, requestPath));
        await AuthorizeAsync(request, configuration, cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<PagedBusinessGroupResponse>(cancellationToken: cancellationToken);
        return payload?.Items ?? new List<BusinessGroupSummary>();
    }

    public async Task<int> ReplaceProductCatalogAsync(
        IReadOnlyList<ProductCatalogEntry> entries,
        string sourceFileName,
        MainApiConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUri(configuration.BaseUrl, "/api/product-catalog"))
        {
            Content = JsonContent.Create(new ReplaceProductCatalogRequest
            {
                SourceFileName = sourceFileName,
                Entries = entries.Select(item => new ProductCatalogEntryRequest
                {
                    ProductCode = item.ProductCode,
                    ProductName = item.ProductName,
                    SpecCode = item.SpecCode,
                    Barcode = item.Barcode,
                    BaseName = item.BaseName,
                    SpecificationToken = item.SpecificationToken,
                    ModelToken = item.ModelToken,
                    Degree = item.Degree,
                    SearchText = item.SearchText
                }).ToList()
            })
        };

        await AuthorizeAsync(request, configuration, cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<ProductCatalogSyncResponse>(cancellationToken: cancellationToken);
        return payload?.EntryCount ?? 0;
    }

    public async Task SyncUploadAsync(
        OrderDraft draft,
        MainApiConfiguration configuration,
        string externalRequestJson,
        string externalResponseJson,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(configuration.BaseUrl, "/api/uploads"))
        {
            Content = JsonContent.Create(new CreateUploadRequest
            {
                DraftId = draft.DraftId,
                OrderNumber = draft.OrderNumber,
                SessionId = draft.SessionId,
                UploaderLoginName = draft.OperatorLoginName,
                BusinessGroupId = draft.BusinessGroupId,
                BusinessGroupName = draft.BusinessGroupName,
                ReceiverName = draft.ReceiverName,
                ReceiverMobile = draft.ReceiverMobile,
                ReceiverAddress = draft.ReceiverAddress,
                Remark = draft.Remark,
                HasGift = draft.HasGift,
                Status = draft.Status,
                StatusDetail = draft.StatusDetail,
                ExternalRequestJson = externalRequestJson,
                ExternalResponseJson = externalResponseJson,
                Items = draft.Items.Select(item => new CreateUploadItemRequest
                {
                    SourceText = item.SourceText,
                    ProductCode = item.ProductCode,
                    ProductName = item.ProductName,
                    Quantity = int.TryParse(item.QuantityText, out var quantity) ? Math.Max(1, quantity) : 1,
                    DegreeText = item.DegreeText,
                    WearPeriod = item.WearPeriod,
                    Remark = item.Remark,
                    IsTrial = item.IsTrial
                }).ToList()
            })
        };

        await AuthorizeAsync(request, configuration, cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task AuthorizeAsync(HttpRequestMessage request, MainApiConfiguration configuration, CancellationToken cancellationToken)
    {
        var accessToken = await GetTokenAsync(configuration, cancellationToken);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        var machineCode = ResolveMachineCode(configuration);
        if (!string.IsNullOrWhiteSpace(machineCode))
        {
            request.Headers.TryAddWithoutValidation("X-Machine-Code", machineCode);
        }
    }

    private async Task<string> GetTokenAsync(MainApiConfiguration configuration, CancellationToken cancellationToken)
    {
        await EnsureSessionAsync(configuration, cancellationToken);
        return _token;
    }

    private async Task EnsureSessionAsync(MainApiConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!configuration.IsEnabled)
        {
            throw new InvalidOperationException("MainApi 联动配置不完整，请补全主服务地址、账号和密码。");
        }

        var cacheKey = BuildCacheKey(configuration);
        if (string.Equals(_cacheKey, cacheKey, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(_cachedUser.LoginName) &&
            _sessionExpiresAtUtc > DateTime.UtcNow.AddSeconds(10))
        {
            return;
        }

        var machineCode = ResolveMachineCode(configuration);
        var machineStatus = await ValidateMachineCodeAsync(configuration, machineCode, cancellationToken);
        if (!machineStatus.Exists)
        {
            throw new InvalidOperationException("机器码未注册。");
        }

        if (!machineStatus.IsActive)
        {
            throw new InvalidOperationException("机器码已禁用。");
        }

        var response = await LoginAsync(configuration, cancellationToken);
        if (response.User is null || string.IsNullOrWhiteSpace(response.User.LoginName))
        {
            throw new InvalidOperationException("MainApi 登录成功，但用户信息为空。");
        }

        _cacheKey = cacheKey;
        _token = response.Token?.Trim() ?? string.Empty;
        _cachedUser = CloneUser(response.User);

        if (!string.IsNullOrWhiteSpace(_token) && response.ExpiresAtUtc > DateTime.UtcNow)
        {
            _sessionExpiresAtUtc = response.ExpiresAtUtc;
        }
        else
        {
            _sessionExpiresAtUtc = DateTime.UtcNow.Add(SessionCacheDurationWhenTokenMissing);
        }
    }

    private static MainApiLoginUser CloneUser(MainApiLoginUser user)
    {
        return new MainApiLoginUser
        {
            Id = user.Id,
            LoginName = user.LoginName,
            ErpId = user.ErpId,
            DisplayName = user.DisplayName,
            Role = user.Role
        };
    }

    private async Task<LoginResponse> LoginAsync(MainApiConfiguration configuration, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.PostAsJsonAsync(
            BuildUri(configuration.BaseUrl, "/api/auth/login"),
            new PasswordLoginRequest
            {
                LoginName = configuration.LoginName,
                Password = configuration.Password
            },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException("MainApi 登录接口返回为空。");
        }

        return payload;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                {
                    throw new InvalidOperationException(messageElement.GetString());
                }

                if (document.RootElement.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Object)
                {
                    var messages = new List<string>();
                    foreach (var property in errorsElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            var message = item.GetString()?.Trim();
                            if (string.IsNullOrWhiteSpace(message))
                            {
                                continue;
                            }

                            messages.Add($"{property.Name}: {message}");
                        }
                    }

                    if (messages.Count > 0)
                    {
                        throw new InvalidOperationException(string.Join(Environment.NewLine, messages));
                    }
                }

                if (document.RootElement.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
                {
                    throw new InvalidOperationException(titleElement.GetString());
                }
            }
            catch (JsonException)
            {
            }

            throw new InvalidOperationException(payload);
        }

        throw new InvalidOperationException($"MainApi 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}");
    }

    private static string BuildUri(string baseUrl, string path)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        return $"{normalizedBaseUrl}{path}";
    }

    private static string BuildCacheKey(MainApiConfiguration configuration)
    {
        return string.Join("|",
            NormalizeBaseUrl(configuration.BaseUrl),
            configuration.LoginName.Trim(),
            configuration.Password,
            NormalizeMachineCode(configuration.MachineCode));
    }

    private static string ResolveMachineCode(MainApiConfiguration configuration)
    {
        var machineCode = NormalizeMachineCode(configuration.MachineCode);
        return string.IsNullOrWhiteSpace(machineCode)
            ? MachineCodeHelper.GetMacByNetworkInterface()
            : machineCode;
    }

    private static string NormalizeMachineCode(string? machineCode)
    {
        return machineCode?.Trim() ?? string.Empty;
    }

    private static string NormalizeBaseUrl(string? baseUrl)
    {
        var value = baseUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value.TrimEnd('/');
        }

        var path = uri.AbsolutePath;
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase))
        {
            path = "/";
        }

        var builder = new UriBuilder(uri)
        {
            Path = path,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private sealed class PasswordLoginRequest
    {
        public string LoginName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }

    private sealed class LoginResponse
    {
        public string Token { get; set; } = string.Empty;

        public DateTime ExpiresAtUtc { get; set; }

        public MainApiLoginUser User { get; set; } = new();
    }

    public sealed class MainApiLoginUser
    {
        public long Id { get; set; }

        public string LoginName { get; set; } = string.Empty;

        public string ErpId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;
    }

    public sealed class BusinessGroupSummary
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Balance { get; set; }

        public int OrderCount { get; set; }
    }

    public sealed class MachineCodeValidationResult
    {
        public string Code { get; set; } = string.Empty;

        public bool Exists { get; set; }

        public bool IsActive { get; set; }

        public long? Id { get; set; }

        public string Description { get; set; } = string.Empty;
    }

    public sealed class UploadQueryResult
    {
        public int TotalCount { get; set; }

        public int PageNumber { get; set; }

        public int PageSize { get; set; }

        public List<UploadSummaryItem> Items { get; set; } = new();
    }

    public sealed class UploadSummaryItem
    {
        public long Id { get; set; }

        public string UploadNo { get; set; } = string.Empty;

        public string DraftId { get; set; } = string.Empty;

        public string OrderNumber { get; set; } = string.Empty;

        public string SessionId { get; set; } = string.Empty;

        public long? BusinessGroupId { get; set; }

        public string BusinessGroupName { get; set; } = string.Empty;

        public string UploaderLoginName { get; set; } = string.Empty;

        public string MachineCode { get; set; } = string.Empty;

        public string ReceiverName { get; set; } = string.Empty;

        public string ReceiverMobile { get; set; } = string.Empty;

        public string ReceiverAddress { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string StatusDetail { get; set; } = string.Empty;

        public int ItemCount { get; set; }

        public int CreatedOn { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public sealed class UploadDetailResult
    {
        public long Id { get; set; }

        public string UploadNo { get; set; } = string.Empty;

        public string DraftId { get; set; } = string.Empty;

        public string OrderNumber { get; set; } = string.Empty;

        public string SessionId { get; set; } = string.Empty;

        public long? BusinessGroupId { get; set; }

        public string BusinessGroupName { get; set; } = string.Empty;

        public string UploaderLoginName { get; set; } = string.Empty;

        public string UploaderDisplayName { get; set; } = string.Empty;

        public string UploaderErpId { get; set; } = string.Empty;

        public string UploaderWecomId { get; set; } = string.Empty;

        public string MachineCode { get; set; } = string.Empty;

        public string ReceiverName { get; set; } = string.Empty;

        public string ReceiverMobile { get; set; } = string.Empty;

        public string ReceiverAddress { get; set; } = string.Empty;

        public bool HasGift { get; set; }

        public string Status { get; set; } = string.Empty;

        public string StatusDetail { get; set; } = string.Empty;

        public int ItemCount { get; set; }

        public int CreatedOn { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public string Remark { get; set; } = string.Empty;

        public string ExternalRequestJson { get; set; } = string.Empty;

        public string ExternalResponseJson { get; set; } = string.Empty;

        public DateTime UpdatedAtUtc { get; set; }

        public List<UploadDetailItem> Items { get; set; } = new();
    }

    public sealed class UploadDetailItem
    {
        public long Id { get; set; }

        public string SourceText { get; set; } = string.Empty;

        public string ProductCode { get; set; } = string.Empty;

        public string ProductName { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public string DegreeText { get; set; } = string.Empty;

        public string WearPeriod { get; set; } = string.Empty;

        public string Remark { get; set; } = string.Empty;

        public bool IsTrial { get; set; }
    }

    private sealed class CreateUploadRequest
    {
        public string DraftId { get; set; } = string.Empty;

        public string OrderNumber { get; set; } = string.Empty;

        public string SessionId { get; set; } = string.Empty;

        public string UploaderLoginName { get; set; } = string.Empty;

        public long? BusinessGroupId { get; set; }

        public string BusinessGroupName { get; set; } = string.Empty;

        public string ReceiverName { get; set; } = string.Empty;

        public string ReceiverMobile { get; set; } = string.Empty;

        public string ReceiverAddress { get; set; } = string.Empty;

        public string Remark { get; set; } = string.Empty;

        public bool HasGift { get; set; }

        public string Status { get; set; } = string.Empty;

        public string StatusDetail { get; set; } = string.Empty;

        public string ExternalRequestJson { get; set; } = string.Empty;

        public string ExternalResponseJson { get; set; } = string.Empty;

        public List<CreateUploadItemRequest> Items { get; set; } = new();
    }

    private sealed class PagedBusinessGroupResponse
    {
        public int TotalCount { get; set; }

        public int PageNumber { get; set; }

        public int PageSize { get; set; }

        public List<BusinessGroupSummary> Items { get; set; } = new();
    }

    private sealed class PagedMachineResponse
    {
        public int TotalCount { get; set; }

        public int PageNumber { get; set; }

        public int PageSize { get; set; }

        public List<MachineListItem> Items { get; set; } = new();
    }

    private sealed class MachineListItem
    {
        public long Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    private sealed class CreateUploadItemRequest
    {
        public string SourceText { get; set; } = string.Empty;

        public string ProductCode { get; set; } = string.Empty;

        public string ProductName { get; set; } = string.Empty;

        public int Quantity { get; set; } = 1;

        public string DegreeText { get; set; } = string.Empty;

        public string WearPeriod { get; set; } = string.Empty;

        public string Remark { get; set; } = string.Empty;

        public bool IsTrial { get; set; }
    }

    private sealed class ReplaceProductCatalogRequest
    {
        public string SourceFileName { get; set; } = string.Empty;

        public List<ProductCatalogEntryRequest> Entries { get; set; } = new();
    }

    private sealed class ProductCatalogEntryRequest
    {
        public string ProductCode { get; set; } = string.Empty;

        public string ProductName { get; set; } = string.Empty;

        public string SpecCode { get; set; } = string.Empty;

        public string Barcode { get; set; } = string.Empty;

        public string BaseName { get; set; } = string.Empty;

        public string SpecificationToken { get; set; } = string.Empty;

        public string ModelToken { get; set; } = string.Empty;

        public string Degree { get; set; } = string.Empty;

        public string SearchText { get; set; } = string.Empty;
    }

    private sealed class ProductCatalogSyncResponse
    {
        public int EntryCount { get; set; }
    }
}
