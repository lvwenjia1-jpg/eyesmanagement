using System.Runtime.InteropServices;
using System.Windows;

namespace MachineCodeTool;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshMachineCode();
        TxtMachineCode.Focus();
        TxtMachineCode.SelectAll();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshMachineCode();
        ShowStatus("机器码已刷新。");
    }

    private async void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        var machineCode = TxtMachineCode.Text.Trim();
        if (string.IsNullOrWhiteSpace(machineCode))
        {
            ShowStatus("机器码为空，无法复制。");
            return;
        }

        if (await TryCopyToClipboardAsync(machineCode))
        {
            ShowStatus("机器码已复制到剪贴板。");
            return;
        }

        ShowStatus("复制失败，剪贴板正忙，请稍后重试。");
    }

    private static async Task<bool> TryCopyToClipboardAsync(string text)
    {
        const int maxRetryCount = 6;
        const int retryDelayMs = 80;

        for (var attempt = 1; attempt <= maxRetryCount; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (COMException)
            {
                if (attempt < maxRetryCount)
                {
                    await Task.Delay(retryDelayMs);
                    continue;
                }
            }
            catch (ExternalException)
            {
                if (attempt < maxRetryCount)
                {
                    await Task.Delay(retryDelayMs);
                    continue;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        return false;
    }

    private void RefreshMachineCode()
    {
        TxtMachineCode.Text = MachineCodeProvider.GetMachineCode();
    }

    private void ShowStatus(string message)
    {
        Title = $"机器码获取工具 - {message}";
    }
}
