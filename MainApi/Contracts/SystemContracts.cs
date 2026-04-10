namespace MainApi.Contracts;

public sealed class SystemStatusResponse
{
    public string ServiceName { get; set; } = string.Empty;

    public string EnvironmentName { get; set; } = string.Empty;

    public DatabaseStatusResponse Database { get; set; } = new();

    public DateTime ServerTimeUtc { get; set; }
}

public sealed class DatabaseStatusResponse
{
    public string Provider { get; set; } = string.Empty;

    public bool IsConnected { get; set; }

    public int UserCount { get; set; }

    public int MachineCodeCount { get; set; }

    public int UploadCount { get; set; }
}
