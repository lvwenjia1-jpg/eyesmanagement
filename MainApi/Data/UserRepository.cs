using System.Globalization;
using MainApi.Domain;
using MySqlConnector;

namespace MainApi.Data;

public sealed class UserRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public UserRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await QueryAsync(new UserQuery { PageNumber = 1, PageSize = 500 }, cancellationToken);
        return result.Items;
    }

    public async Task<PagedQueryResult<UserRecord>> QueryAsync(UserQuery query, CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(query);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var (whereSql, parameters) = BuildWhereClause(normalizedQuery);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(1) FROM users u{whereSql};";
        foreach (var parameter in parameters)
        {
            countCommand.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }

        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                u.id,
                u.login_name,
                u.password_hash,
                u.password_salt,
                u.erp_id,
                u.role,
                u.is_active,
                u.created_at_utc
            FROM users u
            {whereSql}
            ORDER BY u.created_at_utc DESC, u.id DESC
            LIMIT @limit OFFSET @offset;
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }
        command.Parameters.AddWithValue("@limit", normalizedQuery.PageSize);
        command.Parameters.AddWithValue("@offset", (normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var users = new List<UserRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(MapUser(reader));
        }

        return new PagedQueryResult<UserRecord>
        {
            TotalCount = totalCount,
            PageNumber = normalizedQuery.PageNumber,
            PageSize = normalizedQuery.PageSize,
            Items = users
        };
    }

    public async Task<UserRecord?> FindByLoginNameAsync(string loginName, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                u.id,
                u.login_name,
                u.password_hash,
                u.password_salt,
                u.erp_id,
                u.role,
                u.is_active,
                u.created_at_utc
            FROM users u
            WHERE u.login_name = @loginName
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@loginName", loginName.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapUser(reader) : null;
    }

    public async Task<UserRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                u.id,
                u.login_name,
                u.password_hash,
                u.password_salt,
                u.erp_id,
                u.role,
                u.is_active,
                u.created_at_utc
            FROM users u
            WHERE u.id = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapUser(reader) : null;
    }

    public async Task<long> CreateAsync(
        string loginName,
        string passwordSalt,
        string passwordHash,
        string erpId,
        string role,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (login_name, password_hash, password_salt, erp_id, role, is_active, created_at_utc)
            VALUES (@loginName, @passwordHash, @passwordSalt, @erpId, @role, 1, @createdAtUtc);
            """;
        command.Parameters.AddWithValue("@loginName", loginName.Trim());
        command.Parameters.AddWithValue("@passwordHash", passwordHash);
        command.Parameters.AddWithValue("@passwordSalt", passwordSalt);
        command.Parameters.AddWithValue("@erpId", erpId.Trim());
        command.Parameters.AddWithValue("@role", string.IsNullOrWhiteSpace(role) ? "user" : role.Trim());
        command.Parameters.AddWithValue("@createdAtUtc", FormatDate(DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return command.LastInsertedId;
    }

    public async Task UpdateAsync(
        long id,
        string loginName,
        string erpId,
        bool isActive,
        string? passwordSalt,
        string? passwordHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE users
            SET login_name = @loginName,
                erp_id = @erpId,
                is_active = @isActive,
                password_salt = COALESCE(@passwordSalt, password_salt),
                password_hash = COALESCE(@passwordHash, password_hash)
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@loginName", loginName.Trim());
        command.Parameters.AddWithValue("@erpId", erpId.Trim());
        command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
        command.Parameters.AddWithValue("@passwordSalt", string.IsNullOrWhiteSpace(passwordSalt) ? DBNull.Value : passwordSalt);
        command.Parameters.AddWithValue("@passwordHash", string.IsNullOrWhiteSpace(passwordHash) ? DBNull.Value : passwordHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE users
            SET is_active = 0
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddLoginLogAsync(
        long? userId,
        string loginName,
        string machineCode,
        bool isSuccess,
        string message,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO login_logs (user_id, login_name, machine_code, is_success, message)
            VALUES (@userId, @loginName, @machineCode, @isSuccess, @message);
            """;
        command.Parameters.AddWithValue("@userId", userId is null ? DBNull.Value : userId.Value);
        command.Parameters.AddWithValue("@loginName", loginName.Trim());
        command.Parameters.AddWithValue("@machineCode", machineCode.Trim());
        command.Parameters.AddWithValue("@isSuccess", isSuccess ? 1 : 0);
        command.Parameters.AddWithValue("@message", message.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static UserRecord MapUser(MySqlDataReader reader)
    {
        return new UserRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            LoginName = reader.GetString(reader.GetOrdinal("login_name")),
            PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
            PasswordSalt = reader.GetString(reader.GetOrdinal("password_salt")),
            ErpId = reader.GetString(reader.GetOrdinal("erp_id")),
            Role = reader.GetString(reader.GetOrdinal("role")),
            IsActive = reader.GetInt64(reader.GetOrdinal("is_active")) == 1,
            CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc")
        };
    }

    private static UserQuery NormalizeQuery(UserQuery query)
    {
        return new UserQuery
        {
            PageNumber = Math.Max(1, query.PageNumber),
            PageSize = Math.Clamp(query.PageSize, 1, 500),
            Keyword = query.Keyword.Trim(),
            Role = query.Role.Trim(),
            IsActive = query.IsActive
        };
    }

    private static (string WhereSql, Dictionary<string, object> Parameters) BuildWhereClause(UserQuery query)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            clauses.Add("(u.login_name LIKE @keyword OR u.erp_id LIKE @keyword)");
            parameters["@keyword"] = $"%{query.Keyword}%";
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            clauses.Add("u.role = @role");
            parameters["@role"] = query.Role;
        }

        if (query.IsActive.HasValue)
        {
            clauses.Add("u.is_active = @isActive");
            parameters["@isActive"] = query.IsActive.Value ? 1 : 0;
        }

        return clauses.Count == 0
            ? (string.Empty, parameters)
            : ($" WHERE {string.Join(" AND ", clauses)}", parameters);
    }

    private static DateTime ParseDate(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}



