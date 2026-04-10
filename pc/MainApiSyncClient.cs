using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderTextTrainer.Core.Models;

namespace WpfApp11;

public sealed class MainApiSyncClient
{
    private static readonly HttpClient HttpClient = new();

    private string _token = string.Empty;
    private DateTime _expiresAtUtc = DateTime.MinValue;
    private string _cacheKey = string.Empty;

    public async Task<MainApiLoginUser> ValidateLoginAsync(MainApiConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var response = await LoginAsync(configuration, cancellationToken);
        return response.User;
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private async Task<string> GetTokenAsync(MainApiConfiguration configuration, CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(configuration);
        if (string.Equals(_cacheKey, cacheKey, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(_token) &&
            _expiresAtUtc > DateTime.UtcNow.AddMinutes(1))
        {
            return _token;
        }

        var response = await LoginAsync(configuration, cancellationToken);
        _cacheKey = cacheKey;
        _token = response.Token;
        _expiresAtUtc = response.ExpiresAtUtc;
        return _token;
    }

    private async Task<LoginResponse> LoginAsync(MainApiConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!configuration.IsEnabled)
        {
            throw new InvalidOperationException("MainApi 联动配置不完整，请补全接口地址、账号、密码和机器码。");
        }

        using var response = await HttpClient.PostAsJsonAsync(
            BuildUri(configuration.BaseUrl, "/api/auth/login"),
            new LoginRequest
            {
                LoginName = configuration.LoginName,
                Password = configuration.Password,
                MachineCode = configuration.MachineCode
            },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Token))
        {
            throw new InvalidOperationException("MainApi 登录成功，但未返回有效令牌。");
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
        return $"{baseUrl.TrimEnd('/')}{path}";
    }

    private static string BuildCacheKey(MainApiConfiguration configuration)
    {
        return string.Join("|", configuration.BaseUrl.Trim(), configuration.LoginName.Trim(), configuration.Password, configuration.MachineCode.Trim());
    }

    private sealed class LoginRequest
    {
        public string LoginName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string MachineCode { get; set; } = string.Empty;
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

        public string DisplayName { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;
    }

    private sealed class CreateUploadRequest
    {
        public string DraftId { get; set; } = string.Empty;

        public string OrderNumber { get; set; } = string.Empty;

        public string SessionId { get; set; } = string.Empty;

        public string UploaderLoginName { get; set; } = string.Empty;

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
