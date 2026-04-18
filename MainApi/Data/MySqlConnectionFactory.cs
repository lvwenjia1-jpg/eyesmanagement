using MySqlConnector;

namespace MainApi.Data;

public sealed class MySqlConnectionFactory
{
    private readonly MySqlConnectionStringBuilder _builder;
    private readonly SemaphoreSlim _databaseEnsureLock = new(1, 1);
    private bool _databaseEnsured;

    public MySqlConnectionFactory(IConfiguration configuration)
    {
        var rawConnectionString =
            configuration.GetConnectionString("MainDb")
            ?? "Server=127.0.0.1;Port=3306;Database=mainapi;User ID=test;Password=1234;SslMode=None;AllowPublicKeyRetrieval=True;";

        _builder = new MySqlConnectionStringBuilder(rawConnectionString);

        if (string.IsNullOrWhiteSpace(_builder.Server))
        {
            _builder.Server = "127.0.0.1";
        }

        if (_builder.Port == 0)
        {
            _builder.Port = 3306;
        }

        if (string.IsNullOrWhiteSpace(_builder.Database))
        {
            _builder.Database = "mainapi";
        }

        if (string.IsNullOrWhiteSpace(_builder.UserID))
        {
            _builder.UserID = "test";
        }
    }

    public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseExistsAsync(cancellationToken);

        var connection = new MySqlConnection(_builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task EnsureDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        if (_databaseEnsured)
        {
            return;
        }

        await _databaseEnsureLock.WaitAsync(cancellationToken);
        try
        {
            if (_databaseEnsured)
            {
                return;
            }

            var adminBuilder = new MySqlConnectionStringBuilder(_builder.ConnectionString)
            {
                Database = string.Empty
            };

            await using var adminConnection = new MySqlConnection(adminBuilder.ConnectionString);
            await adminConnection.OpenAsync(cancellationToken);

            var databaseNameEscaped = _builder.Database.Replace("`", "``", StringComparison.Ordinal);
            await using var createDatabase = adminConnection.CreateCommand();
            createDatabase.CommandText =
                $"CREATE DATABASE IF NOT EXISTS `{databaseNameEscaped}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await createDatabase.ExecuteNonQueryAsync(cancellationToken);

            _databaseEnsured = true;
        }
        finally
        {
            _databaseEnsureLock.Release();
        }
    }
}
