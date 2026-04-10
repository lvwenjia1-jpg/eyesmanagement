using System.Globalization;
using MainApi.Domain;
using Microsoft.Data.Sqlite;

namespace MainApi.Data;

public sealed class UserRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public UserRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                u.id,
                u.login_name,
                u.display_name,
                u.password_hash,
                u.password_salt,
                u.erp_id,
                u.wecom_id,
                u.role,
                u.is_active,
                u.created_at_utc
            FROM users u
            ORDER BY u.created_at_utc DESC, u.id DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var users = new List<UserRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(MapUser(reader));
        }

        return users;
    }

    public async Task<UserRecord?> FindByLoginNameAsync(string loginName, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                u.id,
                u.login_name,
                u.display_name,
                u.password_hash,
                u.password_salt,
                u.erp_id,
                u.wecom_id,
                u.role,
                u.is_active,
                u.created_at_utc
            FROM users u
            WHERE u.login_name = $loginName
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$loginName", loginName.Trim());

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
                u.display_name,
                u.password_hash,
                u.password_salt,
                u.erp_id,
                u.wecom_id,
                u.role,
                u.is_active,
                u.created_at_utc
            FROM users u
            WHERE u.id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapUser(reader) : null;
    }

    public async Task<long> CreateAsync(
        string loginName,
        string displayName,
        string passwordSalt,
        string passwordHash,
        string erpId,
        string wecomId,
        string role,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (login_name, display_name, password_hash, password_salt, erp_id, wecom_id, role, is_active)
            VALUES ($loginName, $displayName, $passwordHash, $passwordSalt, $erpId, $wecomId, $role, 1);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$loginName", loginName.Trim());
        command.Parameters.AddWithValue("$displayName", displayName.Trim());
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        command.Parameters.AddWithValue("$passwordSalt", passwordSalt);
        command.Parameters.AddWithValue("$erpId", erpId.Trim());
        command.Parameters.AddWithValue("$wecomId", wecomId.Trim());
        command.Parameters.AddWithValue("$role", string.IsNullOrWhiteSpace(role) ? "user" : role.Trim());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateAsync(
        long id,
        string displayName,
        string erpId,
        string wecomId,
        string role,
        bool isActive,
        string? passwordSalt,
        string? passwordHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE users
            SET display_name = $displayName,
                erp_id = $erpId,
                wecom_id = $wecomId,
                role = $role,
                is_active = $isActive,
                password_salt = COALESCE($passwordSalt, password_salt),
                password_hash = COALESCE($passwordHash, password_hash)
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$displayName", displayName.Trim());
        command.Parameters.AddWithValue("$erpId", erpId.Trim());
        command.Parameters.AddWithValue("$wecomId", wecomId.Trim());
        command.Parameters.AddWithValue("$role", string.IsNullOrWhiteSpace(role) ? "user" : role.Trim());
        command.Parameters.AddWithValue("$isActive", isActive ? 1 : 0);
        command.Parameters.AddWithValue("$passwordSalt", string.IsNullOrWhiteSpace(passwordSalt) ? DBNull.Value : passwordSalt);
        command.Parameters.AddWithValue("$passwordHash", string.IsNullOrWhiteSpace(passwordHash) ? DBNull.Value : passwordHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE users
            SET is_active = 0
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
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
            VALUES ($userId, $loginName, $machineCode, $isSuccess, $message);
            """;
        command.Parameters.AddWithValue("$userId", userId is null ? DBNull.Value : userId.Value);
        command.Parameters.AddWithValue("$loginName", loginName.Trim());
        command.Parameters.AddWithValue("$machineCode", machineCode.Trim());
        command.Parameters.AddWithValue("$isSuccess", isSuccess ? 1 : 0);
        command.Parameters.AddWithValue("$message", message.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static UserRecord MapUser(SqliteDataReader reader)
    {
        return new UserRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            LoginName = reader.GetString(reader.GetOrdinal("login_name")),
            DisplayName = reader.GetString(reader.GetOrdinal("display_name")),
            PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
            PasswordSalt = reader.GetString(reader.GetOrdinal("password_salt")),
            ErpId = reader.GetString(reader.GetOrdinal("erp_id")),
            WecomId = reader.GetString(reader.GetOrdinal("wecom_id")),
            Role = reader.GetString(reader.GetOrdinal("role")),
            IsActive = reader.GetInt64(reader.GetOrdinal("is_active")) == 1,
            CreatedAtUtc = ParseDate(reader.GetString(reader.GetOrdinal("created_at_utc")))
        };
    }

    private static DateTime ParseDate(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
