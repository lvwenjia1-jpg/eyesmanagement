using System.Windows;
using System.Windows.Input;

namespace WpfApp11;

public partial class LoginWindow : Window
{
    private readonly WorkflowSettingsRepository _settingsRepository = new();
    private readonly MainApiSyncClient _mainApiSyncClient = new();
    private WorkflowSettingsSnapshot _snapshot = new();
    private bool _isSubmitting;

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += LoginWindow_Loaded;
    }

    public MainApiSession? Session { get; private set; }

    private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _snapshot = _settingsRepository.LoadOrCreate();
        var config = _snapshot.MainApi ?? new MainApiConfiguration();
        TxtLoginName.Text = config.LoginName;
        TxtLoginStatus.Text = $"将连接主服务：{config.BaseUrl}";
        Keyboard.Focus(string.IsNullOrWhiteSpace(TxtLoginName.Text) ? TxtLoginName : TxtPassword);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_isSubmitting)
        {
            return;
        }

        var loginName = TxtLoginName.Text.Trim();
        var password = TxtPassword.Password;
        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password))
        {
            TxtLoginStatus.Text = "请输入登录账号和密码。";
            return;
        }

        var config = _snapshot.MainApi ?? new MainApiConfiguration();
        config.LoginName = loginName;
        config.Password = password;
        config.MachineCode = GetLocalMachineCode();

        try
        {
            SetSubmitting(true, "正在验证机器码和账号，请稍候...");
            var user = await _mainApiSyncClient.ValidateLoginAsync(config);
            _snapshot.MainApi = config;
            _settingsRepository.Save(_snapshot);
            Session = new MainApiSession
            {
                Configuration = config,
                User = user
            };
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            TxtLoginStatus.Text = ex.Message;
        }
        finally
        {
            SetSubmitting(false, TxtLoginStatus.Text);
        }
    }

    private void SetSubmitting(bool isSubmitting, string statusText)
    {
        _isSubmitting = isSubmitting;
        BtnLogin.IsEnabled = !isSubmitting;
        BtnCancel.IsEnabled = !isSubmitting;
        TxtLoginName.IsEnabled = !isSubmitting;
        TxtPassword.IsEnabled = !isSubmitting;
        TxtLoginStatus.Text = statusText;
    }

    private static string GetLocalMachineCode()
    {
        return MachineCodeHelper.GetMacByNetworkInterface();
    }
}
