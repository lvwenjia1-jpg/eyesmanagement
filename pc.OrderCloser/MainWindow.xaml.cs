using System.IO;
using System.Text.Json;
using System.Windows;
using WpfApp11;

namespace OrderCloser;

public partial class MainWindow : Window
{
    private readonly HupunB2cTradeUploader _uploader = new();
    private readonly UploadConfiguration _configuration = LoadConfiguration();

    public MainWindow()
    {
        InitializeComponent();
        TxtTradeId.Focus();
    }

    private async void BtnQueryAndClose_Click(object sender, RoutedEventArgs e)
    {
        var tradeId = TxtTradeId.Text.Trim();
        if (string.IsNullOrWhiteSpace(tradeId))
        {
            MessageBox.Show(this, "请输入原订单推送时使用的 trade_id。", "查询并关闭订单", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtTradeId.Focus();
            return;
        }

        BtnQueryAndClose.IsEnabled = false;
        try
        {
            TxtStatus.Text = "正在查询原交易...";
            var queryResult = await _uploader.QueryTradeListAsync(new OrderDraft { OrderNumber = tradeId }, _configuration);
            TxtResult.Text = queryResult.DebugText;
            if (!queryResult.IsSuccess)
            {
                TxtStatus.Text = "查询失败。";
                return;
            }

            if (!TryExtractCloseOrderIds(queryResult.ResponseText, tradeId, out var orderIds, out var errorMessage))
            {
                TxtResult.Text = $"{queryResult.DebugText}{Environment.NewLine}{Environment.NewLine}{errorMessage}";
                TxtStatus.Text = "未找到可关闭的订单明细。";
                return;
            }

            var confirmed = MessageBox.Show(
                this,
                $"trade_id: {tradeId}{Environment.NewLine}待关闭明细: {orderIds.Count} 条{Environment.NewLine}{Environment.NewLine}确认后将关闭全部原始 order_id。",
                "确认关闭订单",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmed != MessageBoxResult.Yes)
            {
                TxtStatus.Text = "已取消关闭操作。";
                return;
            }

            TxtStatus.Text = "正在提交关闭请求...";
            var closeResult = await _uploader.CloseOrderAsync(tradeId, orderIds, _configuration);
            TxtResult.Text = BuildCloseOrderDisplayText(tradeId, orderIds, closeResult);
            TxtStatus.Text = closeResult.IsSuccess ? "关闭请求成功。" : "关闭请求返回失败。";
        }
        catch (Exception ex)
        {
            TxtResult.Text = ex.ToString();
            TxtStatus.Text = "关闭请求异常。";
        }
        finally
        {
            BtnQueryAndClose.IsEnabled = true;
        }
    }

    private static UploadConfiguration LoadConfiguration()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "SmartRecognitionData", "workflow-settings.json");
        if (!File.Exists(settingsPath))
        {
            return new UploadConfiguration();
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<WorkflowSettingsSnapshot>(File.ReadAllText(settingsPath));
            return snapshot?.Upload ?? new UploadConfiguration();
        }
        catch
        {
            return new UploadConfiguration();
        }
    }

    private static bool TryExtractCloseOrderIds(string responseText, string tradeId, out IReadOnlyList<string> orderIds, out string errorMessage)
    {
        orderIds = Array.Empty<string>();
        errorMessage = string.Empty;
        if (!TryGetTradeData(responseText, out var data))
        {
            errorMessage = "万里牛订单查询没有返回有效的交易数据。";
            return false;
        }

        var matchingTrades = data.EnumerateArray()
            .Where(trade => IsMatchingTrade(trade, tradeId))
            .ToArray();
        if (matchingTrades.Length != 1)
        {
            errorMessage = matchingTrades.Length == 0
                ? $"未查询到 trade_id 为“{tradeId}”的原交易。"
                : $"查询到 {matchingTrades.Length} 笔匹配交易，已停止关闭操作。";
            return false;
        }

        if (!matchingTrades[0].TryGetProperty("orders", out var orders) || orders.ValueKind != JsonValueKind.Array)
        {
            errorMessage = "查询到的交易没有订单明细。";
            return false;
        }

        var ids = orders.EnumerateArray()
            .Select(order =>
            {
                var orderId = ReadJsonText(order, "order_id");
                return string.IsNullOrWhiteSpace(orderId) ? ReadJsonText(order, "tp_oid") : orderId;
            })
            .Where(orderId => !string.IsNullOrWhiteSpace(orderId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ids.Length == 0)
        {
            errorMessage = "查询到的交易不包含有效 order_id。";
            return false;
        }

        orderIds = ids;
        return true;
    }

    private static bool TryGetTradeData(string responseText, out JsonElement data)
    {
        data = default;
        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (!document.RootElement.TryGetProperty("data", out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            data = value.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsMatchingTrade(JsonElement trade, string tradeId)
    {
        return string.Equals(ReadJsonText(trade, "trade_no"), tradeId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ReadJsonText(trade, "trade_id"), tradeId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ReadJsonText(trade, "tp_tid"), tradeId, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadJsonText(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? property.ToString().Trim() : string.Empty;
    }

    private static string BuildCloseOrderDisplayText(string tradeId, IReadOnlyList<string> orderIds, HupunUploadAttemptResult result)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "关闭订单",
            $"trade_id: {tradeId}",
            $"orders[].order_id: {orderIds.Count} 条",
            $"请求地址: {result.RequestUrl}",
            $"HTTP 状态: {(int)result.StatusCode}",
            $"业务结果: {(result.IsSuccess ? "成功" : "失败")}",
            string.Empty,
            "请求载荷:",
            result.RequestFields.TryGetValue("trades", out var trades) ? trades : string.Empty,
            string.Empty,
            "响应内容:",
            result.ResponseText
        });
    }
}
