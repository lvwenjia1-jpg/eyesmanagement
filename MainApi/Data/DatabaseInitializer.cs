using MainApi.Options;
using MainApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace MainApi.Data;

public sealed class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly PasswordHasher _passwordHasher;
    private readonly BootstrapAdminOptions _bootstrapAdmin;

    public DatabaseInitializer(
        SqliteConnectionFactory connectionFactory,
        PasswordHasher passwordHasher,
        IOptions<BootstrapAdminOptions> bootstrapAdmin)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _bootstrapAdmin = bootstrapAdmin.Value;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await SeedAdminAsync(connection, cancellationToken);
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                login_name TEXT NOT NULL UNIQUE,
                display_name TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                password_salt TEXT NOT NULL,
                erp_id TEXT NOT NULL,
                wecom_id TEXT NOT NULL,
                role TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS machine_codes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT NOT NULL UNIQUE,
                description TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS user_machine_bindings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                machine_code_id INTEGER NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(user_id, machine_code_id),
                FOREIGN KEY(user_id) REFERENCES users(id),
                FOREIGN KEY(machine_code_id) REFERENCES machine_codes(id)
            );

            CREATE TABLE IF NOT EXISTS login_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NULL,
                login_name TEXT NOT NULL,
                machine_code TEXT NOT NULL,
                is_success INTEGER NOT NULL,
                message TEXT NOT NULL,
                created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(user_id) REFERENCES users(id)
            );

            CREATE TABLE IF NOT EXISTS order_uploads (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                upload_no TEXT NOT NULL UNIQUE,
                draft_id TEXT NOT NULL,
                order_number TEXT NOT NULL,
                session_id TEXT NOT NULL,
                uploader_login_name TEXT NOT NULL,
                uploader_display_name TEXT NOT NULL,
                uploader_erp_id TEXT NOT NULL,
                uploader_wecom_id TEXT NOT NULL,
                machine_code TEXT NOT NULL,
                receiver_name TEXT NOT NULL,
                receiver_mobile TEXT NOT NULL,
                receiver_address TEXT NOT NULL,
                remark TEXT NOT NULL,
                has_gift INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL,
                status_detail TEXT NOT NULL,
                external_request_json TEXT NOT NULL,
                external_response_json TEXT NOT NULL,
                item_count INTEGER NOT NULL DEFAULT 0,
                created_on INTEGER NOT NULL DEFAULT 0,
                created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS order_upload_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                order_upload_id INTEGER NOT NULL,
                source_text TEXT NOT NULL,
                product_code TEXT NOT NULL,
                product_name TEXT NOT NULL,
                quantity INTEGER NOT NULL,
                degree_text TEXT NOT NULL,
                wear_period TEXT NOT NULL,
                remark TEXT NOT NULL,
                is_trial INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(order_upload_id) REFERENCES order_uploads(id) ON DELETE CASCADE
            );
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await EnsureColumnAsync(connection, "order_uploads", "item_count", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "order_uploads", "created_on", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureIndexAsync(
            connection,
            "CREATE INDEX IF NOT EXISTS idx_order_uploads_created_on_id ON order_uploads(created_on DESC, id DESC);",
            cancellationToken);
        await EnsureIndexAsync(
            connection,
            "CREATE INDEX IF NOT EXISTS idx_order_uploads_machine_created_on_id ON order_uploads(machine_code, created_on DESC, id DESC);",
            cancellationToken);
        await EnsureIndexAsync(
            connection,
            "CREATE INDEX IF NOT EXISTS idx_order_uploads_status_created_on_id ON order_uploads(status, created_on DESC, id DESC);",
            cancellationToken);
        await EnsureIndexAsync(
            connection,
            "CREATE INDEX IF NOT EXISTS idx_order_upload_items_order_upload_id ON order_upload_items(order_upload_id);",
            cancellationToken);
        await BackfillUploadSummaryColumnsAsync(connection, cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(connection, tableName, columnName, cancellationToken))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(reader.GetOrdinal("name")), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task EnsureIndexAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task BackfillUploadSummaryColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE order_uploads
            SET created_on = CAST(strftime('%Y%m%d', created_at_utc) AS INTEGER)
            WHERE created_on = 0;

            UPDATE order_uploads
            SET item_count = COALESCE((
                SELECT COUNT(1)
                FROM order_upload_items items
                WHERE items.order_upload_id = order_uploads.id
            ), 0)
            WHERE item_count = 0;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SeedAdminAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var loginName = _bootstrapAdmin.LoginName.Trim();
        if (string.IsNullOrWhiteSpace(loginName))
        {
            return;
        }

        await using var lookupUser = connection.CreateCommand();
        lookupUser.CommandText = "SELECT id FROM users WHERE login_name = $loginName LIMIT 1;";
        lookupUser.Parameters.AddWithValue("$loginName", loginName);
        var userIdValue = await lookupUser.ExecuteScalarAsync(cancellationToken);

        if (userIdValue is null)
        {
            var (salt, hash) = _passwordHasher.HashPassword(_bootstrapAdmin.Password);

            await using var insertUser = connection.CreateCommand();
            insertUser.CommandText = """
                INSERT INTO users (login_name, display_name, password_hash, password_salt, erp_id, wecom_id, role, is_active)
                VALUES ($loginName, $displayName, $passwordHash, $passwordSalt, $erpId, $wecomId, $role, 1);
                SELECT last_insert_rowid();
                """;
            insertUser.Parameters.AddWithValue("$loginName", loginName);
            insertUser.Parameters.AddWithValue("$displayName", _bootstrapAdmin.DisplayName.Trim());
            insertUser.Parameters.AddWithValue("$passwordHash", hash);
            insertUser.Parameters.AddWithValue("$passwordSalt", salt);
            insertUser.Parameters.AddWithValue("$erpId", _bootstrapAdmin.ErpId.Trim());
            insertUser.Parameters.AddWithValue("$wecomId", _bootstrapAdmin.WecomId.Trim());
            insertUser.Parameters.AddWithValue("$role", string.IsNullOrWhiteSpace(_bootstrapAdmin.Role) ? "admin" : _bootstrapAdmin.Role.Trim());
            await insertUser.ExecuteScalarAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(_bootstrapAdmin.MachineCode))
        {
            return;
        }

        await using var lookupMachine = connection.CreateCommand();
        lookupMachine.CommandText = "SELECT id FROM machine_codes WHERE code = $code LIMIT 1;";
        lookupMachine.Parameters.AddWithValue("$code", _bootstrapAdmin.MachineCode.Trim());
        var machineIdValue = await lookupMachine.ExecuteScalarAsync(cancellationToken);

        if (machineIdValue is null)
        {
            await using var insertMachine = connection.CreateCommand();
            insertMachine.CommandText = """
                INSERT INTO machine_codes (code, description, is_active)
                VALUES ($code, $description, 1);
                SELECT last_insert_rowid();
            """;
            insertMachine.Parameters.AddWithValue("$code", _bootstrapAdmin.MachineCode.Trim());
            insertMachine.Parameters.AddWithValue("$description", _bootstrapAdmin.MachineDescription.Trim());
            await insertMachine.ExecuteScalarAsync(cancellationToken);
        }
    }
}
