using System.Windows;
using System.Windows.Threading;

namespace WpfApp11;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var loginWindow = new LoginWindow();
        var loginResult = loginWindow.ShowDialog();
        if (loginResult != true)
        {
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow(loginWindow.Session);
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.ToString(),
            "WpfApp11 未处理异常",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            MessageBox.Show(
                exception.ToString(),
                "WpfApp11 致命异常",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
