using MainApi.Contracts;

namespace MainApi.Data;

public sealed class SystemRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IWebHostEnvironment _environment;

    public SystemRepository(SqliteConnectionFactory connectionFactory, IWebHostEnvironment environment)
    {
        _connectionFactory = connectionFactory;
        _environment = environment;
    }

    public async Task<SystemStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        return new SystemStatusResponse
        {
            ServiceName = "MainApi",
            EnvironmentName = _environment.EnvironmentName,
            Database = new DatabaseStatusResponse
            {
                Provider = "SQLite",
                IsConnected = true,
                UserCount = await CountAsync(connection, "users", cancellationToken),
                MachineCodeCount = await CountAsync(connection, "machine_codes", cancellationToken),
                UploadCount = await CountAsync(connection, "order_uploads", cancellationToken)
            },
            ServerTimeUtc = DateTime.UtcNow
        };
    }

    private static async Task<int> CountAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM {tableName};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
