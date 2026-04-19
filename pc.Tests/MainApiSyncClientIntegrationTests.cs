using System.Collections.ObjectModel;
using System.Net.Http;
using Xunit;

namespace WpfApp11.Tests;

public sealed class MainApiSyncClientIntegrationTests
{
    [Fact]
    public async Task UploadOrderRecord_ShouldPersistAndBeQueryable()
    {
        var configuration = await BuildConfigurationAsync();
        var client = new MainApiSyncClient();

        var loginUser = await client.ValidateLoginAsync(configuration);
        Assert.False(string.IsNullOrWhiteSpace(loginUser.LoginName));

        var draftId = $"itest-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var orderNumber = $"itest-order-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var receiverName = "Integration Receiver";
        var receiverMobile = "13800138000";
        var receiverAddress = "Test City Test Street 1";
        var status = "已接收";
        var statusDetail = "integration-test";
        var externalRequestJson = "{\"source\":\"itest\"}";
        var externalResponseJson = "{\"result\":\"ok\"}";

        var draft = new OrderDraft
        {
            DraftId = draftId,
            OrderNumber = orderNumber,
            SessionId = "itest-session",
            OperatorLoginName = configuration.LoginName,
            ReceiverName = receiverName,
            ReceiverMobile = receiverMobile,
            ReceiverAddress = receiverAddress,
            Status = status,
            StatusDetail = statusDetail,
            Remark = "integration upload",
            HasGift = true,
            Items = new ObservableCollection<OrderItemDraft>
            {
                new()
                {
                    SourceText = "integration item",
                    ProductCode = "ITEST-PRODUCT-001",
                    ProductName = "Integration Lens",
                    QuantityText = "2",
                    DegreeText = "-1.50",
                    WearPeriod = "Monthly",
                    Remark = "item remark",
                    IsTrial = false
                }
            }
        };

        await client.SyncUploadAsync(draft, configuration, externalRequestJson, externalResponseJson);

        var queryResult = await client.QueryUploadsByAccountAsync(
            configuration,
            configuration.LoginName,
            pageNumber: 1,
            pageSize: 100,
            draftId: draftId);

        Assert.True(queryResult.TotalCount >= 1);
        var summary = Assert.Single(queryResult.Items, item => string.Equals(item.DraftId, draftId, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(orderNumber, summary.OrderNumber);
        Assert.Equal(receiverName, summary.ReceiverName);
        Assert.Equal(receiverMobile, summary.ReceiverMobile);
        Assert.Equal(receiverAddress, summary.ReceiverAddress);
        Assert.Equal(configuration.MachineCode, summary.MachineCode);
        Assert.Equal(status, summary.Status);
        Assert.Equal(statusDetail, summary.StatusDetail);
        Assert.Equal(1, summary.ItemCount);

        var detail = await client.GetUploadByIdAsync(configuration, summary.Id);
        Assert.Equal(summary.Id, detail.Id);
        Assert.Equal(draftId, detail.DraftId);
        Assert.Equal(orderNumber, detail.OrderNumber);
        Assert.Equal(receiverName, detail.ReceiverName);
        Assert.Equal(receiverMobile, detail.ReceiverMobile);
        Assert.Equal(receiverAddress, detail.ReceiverAddress);
        Assert.Equal(configuration.MachineCode, detail.MachineCode);
        Assert.Equal(status, detail.Status);
        Assert.Equal(statusDetail, detail.StatusDetail);
        Assert.Equal(externalRequestJson, detail.ExternalRequestJson);
        Assert.Equal(externalResponseJson, detail.ExternalResponseJson);
        Assert.Single(detail.Items);

        var item = detail.Items[0];
        Assert.Equal("integration item", item.SourceText);
        Assert.Equal("ITEST-PRODUCT-001", item.ProductCode);
        Assert.Equal("Integration Lens", item.ProductName);
        Assert.Equal(2, item.Quantity);
        Assert.Equal("-1.50", item.DegreeText);
        Assert.Equal("Monthly", item.WearPeriod);
        Assert.Equal("item remark", item.Remark);
        Assert.False(item.IsTrial);
    }

    [Fact]
    public async Task Login_ShouldReject_UnregisteredMachineCode()
    {
        var configuration = await BuildConfigurationAsync();
        configuration.MachineCode = $"UNREGISTERED-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var client = new MainApiSyncClient();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ValidateLoginAsync(configuration));

        Assert.True(
            ex.Message.Contains("machine", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("机器", StringComparison.OrdinalIgnoreCase),
            $"异常错误信息：{ex.Message}");
    }

    private static async Task<MainApiConfiguration> BuildConfigurationAsync()
    {
        var loginName = Environment.GetEnvironmentVariable("WPF_TEST_LOGIN")?.Trim();
        var password = Environment.GetEnvironmentVariable("WPF_TEST_PASSWORD")?.Trim();
        var preferredBaseUrl = Environment.GetEnvironmentVariable("WPF_TEST_BASEURL")?.Trim();
        var machineCode = Environment.GetEnvironmentVariable("WPF_TEST_MACHINE_CODE")?.Trim();

        var baseUrl = await ResolveBaseUrlAsync(preferredBaseUrl);
        return new MainApiConfiguration
        {
            BaseUrl = baseUrl,
            LoginName = string.IsNullOrWhiteSpace(loginName) ? "admin" : loginName,
            Password = string.IsNullOrWhiteSpace(password) ? "123456" : password,
            MachineCode = string.IsNullOrWhiteSpace(machineCode) ? "DEMO-PC-001" : machineCode
        };
    }

    private static async Task<string> ResolveBaseUrlAsync(string? preferredBaseUrl)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredBaseUrl))
        {
            candidates.Add(NormalizeBaseUrl(preferredBaseUrl));
        }

        candidates.Add("https://localhost:5001");
        candidates.Add("http://47.107.154.255:98");
        candidates.Add("http://127.0.0.1:5249");
        candidates.Add("http://localhost:5249");
        candidates.Add("https://localhost:7018");

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await IsSystemStatusHealthyAsync(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No reachable MainApi service was found. Start MainApi first.");
    }

    private static async Task<bool> IsSystemStatusHealthyAsync(string baseUrl)
    {
        try
        {
            using var handler = new HttpClientHandler();
            if (baseUrl.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase) ||
                baseUrl.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            using var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            using var response = await httpClient.GetAsync($"{baseUrl.TrimEnd('/')}/api/system/status");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var value = baseUrl.Trim();
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
}
