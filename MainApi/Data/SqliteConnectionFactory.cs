using Microsoft.Data.Sqlite;

namespace MainApi.Data;

public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var rawConnectionString = configuration.GetConnectionString("MainDb") ?? "Data Source=App_Data/mainapi.db";
        var builder = new SqliteConnectionStringBuilder(rawConnectionString);

        if (!string.IsNullOrWhiteSpace(builder.DataSource) && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(environment.ContentRootPath, builder.DataSource));
        }

        var dbDirectory = Path.GetDirectoryName(builder.DataSource);
        if (!string.IsNullOrWhiteSpace(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }

        _connectionString = builder.ToString();
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);

        return connection;
    }
}
