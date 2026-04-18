using System.Globalization;
using MainApi.Domain;
using MySqlConnector;

namespace MainApi.Data;

public sealed class MachineRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public MachineRepository(MySqlConnectionFactory connectionFactory)
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

    public async Task<PagedQueryResult<MachineCodeRecord>> QueryAsync(MachineQuery query, CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(query);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var (whereSql, parameters) = BuildWhereClause(normalizedQuery);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(1) FROM machine_codes m{whereSql};";
        foreach (var parameter in parameters)
        {
            countCommand.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }

        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, code, description, is_active, created_at_utc
            FROM machine_codes m
            {whereSql}
            ORDER BY created_at_utc DESC, id DESC
            LIMIT @limit OFFSET @offset;
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }
        command.Parameters.AddWithValue("@limit", normalizedQuery.PageSize);
        command.Parameters.AddWithValue("@offset", (normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var machines = new List<MachineCodeRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            machines.Add(MapMachine(reader));
        }

        return new PagedQueryResult<MachineCodeRecord>
        {
            TotalCount = totalCount,
            PageNumber = normalizedQuery.PageNumber,
            PageSize = normalizedQuery.PageSize,
            Items = machines
        };
    }

    public async Task<MachineCodeRecord?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, description, is_active, created_at_utc
            FROM machine_codes
            WHERE code = @code
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@code", code.Trim());

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
            WHERE id = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapMachine(reader) : null;
    }

    public async Task<long> CreateAsync(string code, string description, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO machine_codes (code, description, is_active)
            VALUES (@code, @description, 1);
            """;
        command.Parameters.AddWithValue("@code", code.Trim());
        command.Parameters.AddWithValue("@description", description.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return command.LastInsertedId;
    }

    public async Task UpdateAsync(long id, string code, string description, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE machine_codes
            SET code = @code,
                description = @description,
                is_active = @isActive
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@code", code.Trim());
        command.Parameters.AddWithValue("@description", description.Trim());
        command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE machine_codes
            SET is_active = 0
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MachineCodeRecord MapMachine(MySqlDataReader reader)
    {
        return new MachineCodeRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Code = reader.GetString(reader.GetOrdinal("code")),
            Description = reader.GetString(reader.GetOrdinal("description")),
            IsActive = reader.GetInt64(reader.GetOrdinal("is_active")) == 1,
            CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc")
        };
    }

    private static MachineQuery NormalizeQuery(MachineQuery query)
    {
        return new MachineQuery
        {
            PageNumber = Math.Max(1, query.PageNumber),
            PageSize = Math.Clamp(query.PageSize, 1, 500),
            Keyword = query.Keyword.Trim(),
            IsActive = query.IsActive
        };
    }

    private static (string WhereSql, Dictionary<string, object> Parameters) BuildWhereClause(MachineQuery query)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            clauses.Add("(m.code LIKE @keyword OR m.description LIKE @keyword)");
            parameters["@keyword"] = $"%{query.Keyword}%";
        }

        if (query.IsActive.HasValue)
        {
            clauses.Add("m.is_active = @isActive");
            parameters["@isActive"] = query.IsActive.Value ? 1 : 0;
        }

        return clauses.Count == 0
            ? (string.Empty, parameters)
            : ($" WHERE {string.Join(" AND ", clauses)}", parameters);
    }
}



