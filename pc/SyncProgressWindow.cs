using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfApp11;

public sealed class SyncProgressWindow : Window
{
    private readonly TextBlock _messageTextBlock;

    public SyncProgressWindow(string message)
    {
        Title = "正在同步";
        Width = 360;
        Height = 140;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.ToolWindow;
        Background = Brushes.White;

        _messageTextBlock = new TextBlock
        {
            Text = message,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    _messageTextBlock
                }
            }
        };
    }

    public void UpdateMessage(string message)
    {
        _messageTextBlock.Text = message;
    }
}
