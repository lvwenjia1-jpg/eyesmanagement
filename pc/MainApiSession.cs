namespace WpfApp11;

public sealed class MainApiSession
{
    public MainApiConfiguration Configuration { get; init; } = new();

    public MainApiSyncClient.MainApiLoginUser User { get; init; } = new();
}
