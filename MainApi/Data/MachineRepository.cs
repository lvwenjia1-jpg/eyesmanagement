using System.Globalization;
using MainApi.Domain;
using Microsoft.Data.Sqlite;

namespace MainApi.Data;

public sealed class MachineRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public MachineRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<MachineCodeRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, description, is_active, created_at_utc
            FROM machine_codes
            ORDER BY created_at_utc DESC, id DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var machines = new List<MachineCodeRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            machines.Add(MapMachine(reader));
        }

        return machines;
    }

    public async Task<MachineCodeRecord?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, description, is_active, created_at_utc
            FROM machine_codes
            WHERE code = $code
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$code", code.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapMachine(reader) : null;
    }

    public async Task<MachineCodeRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, description, is_active, created_at_utc
            FROM machine_codes
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapMachine(reader) : null;
    }

    public async Task<long> CreateAsync(string code, string description, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO machine_codes (code, description, is_active)
            VALUES ($code, $description, 1);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$code", code.Trim());
        command.Parameters.AddWithValue("$description", description.Trim());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateAsync(long id, string code, string description, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE machine_codes
            SET code = $code,
                description = $description,
                is_active = $isActive
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$code", code.Trim());
        command.Parameters.AddWithValue("$description", description.Trim());
        command.Parameters.AddWithValue("$isActive", isActive ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE machine_codes
            SET is_active = 0
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MachineCodeRecord MapMachine(SqliteDataReader reader)
    {
        return new MachineCodeRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Code = reader.GetString(reader.GetOrdinal("code")),
            Description = reader.GetString(reader.GetOrdinal("description")),
            IsActive = reader.GetInt64(reader.GetOrdinal("is_active")) == 1,
            CreatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at_utc")), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
        };
    }
}
