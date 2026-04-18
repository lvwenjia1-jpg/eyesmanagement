using MainApi.Options;
using MainApi.Services;
using MySqlConnector;
using Microsoft.Extensions.Options;

namespace MainApi.Data;

public sealed class DatabaseInitializer
{
    private readonly MySqlConnectionFactory _connectionFactory;
    private readonly PasswordHasher _passwordHasher;
    private readonly BootstrapAdminOptions _bootstrapAdmin;

    public DatabaseInitializer(
        MySqlConnectionFactory connectionFactory,
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

    private static async Task EnsureSchemaAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var statements = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS users (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                login_name VARCHAR(64) NOT NULL,
                password_hash VARCHAR(256) NOT NULL,
                password_salt VARCHAR(256) NOT NULL,
                erp_id VARCHAR(64) NOT NULL,
                role VARCHAR(32) NOT NULL,
                is_active TINYINT(1) NOT NULL DEFAULT 1,
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uq_users_login_name (login_name)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS machine_codes (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                code VARCHAR(128) NOT NULL,
                description VARCHAR(256) NOT NULL,
                is_active TINYINT(1) NOT NULL DEFAULT 1,
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uq_machine_codes_code (code)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS login_logs (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                user_id BIGINT NULL,
                login_name VARCHAR(64) NOT NULL,
                machine_code VARCHAR(128) NOT NULL,
                is_success TINYINT(1) NOT NULL,
                message VARCHAR(512) NOT NULL,
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                KEY idx_login_logs_user_id (user_id),
                CONSTRAINT fk_login_logs_user_id FOREIGN KEY (user_id) REFERENCES users(id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS business_groups (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(128) NOT NULL,
                balance DECIMAL(18,2) NOT NULL DEFAULT 0,
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uq_business_groups_name (name)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS dashboard_orders (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                order_no VARCHAR(64) NOT NULL,
                business_group_id BIGINT NOT NULL,
                uploader_login_name VARCHAR(64) NOT NULL,
                receiver_name VARCHAR(128) NOT NULL,
                receiver_address VARCHAR(512) NOT NULL,
                amount DECIMAL(18,2) NOT NULL DEFAULT 0,
                tracking_number VARCHAR(128) NOT NULL DEFAULT '',
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uq_dashboard_orders_order_no (order_no),
                KEY idx_dashboard_orders_business_group_id (business_group_id),
                CONSTRAINT fk_dashboard_orders_business_group_id FOREIGN KEY (business_group_id) REFERENCES business_groups(id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS dashboard_order_items (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                order_id BIGINT NOT NULL,
                product_code VARCHAR(64) NOT NULL,
                product_name VARCHAR(256) NOT NULL,
                quantity INT NOT NULL DEFAULT 1,
                KEY idx_dashboard_order_items_order_id (order_id),
                CONSTRAINT fk_dashboard_order_items_order_id FOREIGN KEY (order_id) REFERENCES dashboard_orders(id) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS order_uploads (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                upload_no VARCHAR(64) NOT NULL,
                draft_id VARCHAR(128) NOT NULL,
                order_number VARCHAR(128) NOT NULL,
                session_id VARCHAR(128) NOT NULL,
                business_group_id BIGINT NULL,
                business_group_name VARCHAR(128) NOT NULL DEFAULT '',
                uploader_login_name VARCHAR(64) NOT NULL,
                uploader_display_name VARCHAR(64) NOT NULL,
                uploader_erp_id VARCHAR(64) NOT NULL,
                uploader_wecom_id VARCHAR(64) NOT NULL,
                machine_code VARCHAR(128) NOT NULL,
                receiver_name VARCHAR(128) NOT NULL,
                receiver_mobile VARCHAR(64) NOT NULL,
                receiver_address VARCHAR(512) NOT NULL,
                remark VARCHAR(512) NOT NULL,
                has_gift TINYINT(1) NOT NULL DEFAULT 0,
                status VARCHAR(64) NOT NULL,
                status_detail VARCHAR(512) NOT NULL,
                external_request_json LONGTEXT NOT NULL,
                external_response_json LONGTEXT NOT NULL,
                item_count INT NOT NULL DEFAULT 0,
                created_on INT NOT NULL DEFAULT 0,
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uq_order_uploads_upload_no (upload_no),
                KEY idx_order_uploads_business_group_id (business_group_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS order_upload_items (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                order_upload_id BIGINT NOT NULL,
                source_text VARCHAR(512) NOT NULL,
                product_code VARCHAR(64) NOT NULL,
                product_name VARCHAR(256) NOT NULL,
                quantity INT NOT NULL,
                degree_text VARCHAR(64) NOT NULL,
                wear_period VARCHAR(64) NOT NULL,
                remark VARCHAR(512) NOT NULL,
                is_trial TINYINT(1) NOT NULL DEFAULT 0,
                KEY idx_order_upload_items_order_upload_id (order_upload_id),
                CONSTRAINT fk_order_upload_items_order_upload_id FOREIGN KEY (order_upload_id) REFERENCES order_uploads(id) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS product_catalog_entries (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                product_code VARCHAR(64) NOT NULL,
                product_name VARCHAR(256) NOT NULL,
                spec_code VARCHAR(64) NOT NULL,
                barcode VARCHAR(128) NOT NULL,
                base_name VARCHAR(128) NOT NULL,
                specification_token VARCHAR(128) NOT NULL,
                model_token VARCHAR(128) NOT NULL,
                degree VARCHAR(64) NOT NULL,
                search_text VARCHAR(512) NOT NULL,
                sort_order INT NOT NULL DEFAULT 0,
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uq_product_catalog_entries_product_code (product_code)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """
        };

        foreach (var statement in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureUploadColumnsAsync(connection, cancellationToken);
        await EnsureIndexesAsync(connection, cancellationToken);
        await BackfillUploadSummaryColumnsAsync(connection, cancellationToken);
    }

    private static async Task EnsureUploadColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, "order_uploads", "item_count", "INT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "order_uploads", "created_on", "INT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "order_uploads", "business_group_id", "BIGINT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "order_uploads", "business_group_name", "VARCHAR(128) NOT NULL DEFAULT ''", cancellationToken);
    }

    private static async Task EnsureIndexesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var indexes = new[]
        {
            ("business_groups", "idx_business_groups_name", "CREATE INDEX idx_business_groups_name ON business_groups(name)"),
            ("dashboard_orders", "idx_dashboard_orders_group_created", "CREATE INDEX idx_dashboard_orders_group_created ON dashboard_orders(business_group_id, created_at_utc DESC, id DESC)"),
            ("dashboard_order_items", "idx_dashboard_order_items_order_id", "CREATE INDEX idx_dashboard_order_items_order_id ON dashboard_order_items(order_id)"),
            ("order_uploads", "idx_order_uploads_created_on_id", "CREATE INDEX idx_order_uploads_created_on_id ON order_uploads(created_on DESC, id DESC)"),
            ("order_uploads", "idx_order_uploads_machine_created_on_id", "CREATE INDEX idx_order_uploads_machine_created_on_id ON order_uploads(machine_code, created_on DESC, id DESC)"),
            ("order_uploads", "idx_order_uploads_status_created_on_id", "CREATE INDEX idx_order_uploads_status_created_on_id ON order_uploads(status, created_on DESC, id DESC)"),
            ("order_uploads", "idx_order_uploads_uploader_created_on_id", "CREATE INDEX idx_order_uploads_uploader_created_on_id ON order_uploads(uploader_login_name, created_on DESC, id DESC)"),
            ("order_uploads", "idx_order_uploads_business_group_created_on_id", "CREATE INDEX idx_order_uploads_business_group_created_on_id ON order_uploads(business_group_id, created_on DESC, id DESC)"),
            ("order_upload_items", "idx_order_upload_items_order_upload_id", "CREATE INDEX idx_order_upload_items_order_upload_id ON order_upload_items(order_upload_id)"),
            ("product_catalog_entries", "idx_product_catalog_entries_sort_order_id", "CREATE INDEX idx_product_catalog_entries_sort_order_id ON product_catalog_entries(sort_order ASC, id ASC)")
        };

        foreach (var (tableName, indexName, createSql) in indexes)
        {
            if (await IndexExistsAsync(connection, tableName, indexName, cancellationToken))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = createSql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureColumnAsync(
        MySqlConnection connection,
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
        command.CommandText = $"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {columnDefinition};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(
        MySqlConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = @tableName
              AND column_name = @columnName;
            """;
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> IndexExistsAsync(
        MySqlConnection connection,
        string tableName,
        string indexName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM information_schema.statistics
            WHERE table_schema = DATABASE()
              AND table_name = @tableName
              AND index_name = @indexName;
            """;
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@indexName", indexName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task BackfillUploadSummaryColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var updateCreatedOn = connection.CreateCommand())
        {
            updateCreatedOn.CommandText = """
                UPDATE order_uploads
                SET created_on = CAST(DATE_FORMAT(created_at_utc, '%Y%m%d') AS UNSIGNED)
                WHERE created_on = 0 OR created_on IS NULL;
                """;
            await updateCreatedOn.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var updateItemCount = connection.CreateCommand();
        updateItemCount.CommandText = """
            UPDATE order_uploads uploads
            LEFT JOIN (
                SELECT order_upload_id, COUNT(1) AS total_count
                FROM order_upload_items
                GROUP BY order_upload_id
            ) summary ON summary.order_upload_id = uploads.id
            SET uploads.item_count = COALESCE(summary.total_count, 0)
            WHERE uploads.item_count = 0 OR uploads.item_count IS NULL;
            """;
        await updateItemCount.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SeedAdminAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var loginName = _bootstrapAdmin.LoginName.Trim();
        if (string.IsNullOrWhiteSpace(loginName))
        {
            return;
        }

        await using var lookupUser = connection.CreateCommand();
        lookupUser.CommandText = "SELECT id FROM users WHERE login_name = @loginName LIMIT 1;";
        lookupUser.Parameters.AddWithValue("@loginName", loginName);
        var userIdValue = await lookupUser.ExecuteScalarAsync(cancellationToken);

        if (userIdValue is null)
        {
            var (salt, hash) = _passwordHasher.HashPassword(_bootstrapAdmin.Password);

            await using var insertUser = connection.CreateCommand();
            insertUser.CommandText = """
                INSERT INTO users (login_name, password_hash, password_salt, erp_id, role, is_active, created_at_utc)
                VALUES (@loginName, @passwordHash, @passwordSalt, @erpId, @role, 1, UTC_TIMESTAMP(6));
                """;
            insertUser.Parameters.AddWithValue("@loginName", loginName);
            insertUser.Parameters.AddWithValue("@passwordHash", hash);
            insertUser.Parameters.AddWithValue("@passwordSalt", salt);
            insertUser.Parameters.AddWithValue("@erpId", _bootstrapAdmin.ErpId.Trim());
            insertUser.Parameters.AddWithValue("@role", string.IsNullOrWhiteSpace(_bootstrapAdmin.Role) ? "admin" : _bootstrapAdmin.Role.Trim());
            await insertUser.ExecuteNonQueryAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(_bootstrapAdmin.MachineCode))
        {
            return;
        }

        await using var lookupMachine = connection.CreateCommand();
        lookupMachine.CommandText = "SELECT id FROM machine_codes WHERE code = @code LIMIT 1;";
        lookupMachine.Parameters.AddWithValue("@code", _bootstrapAdmin.MachineCode.Trim());
        var machineIdValue = await lookupMachine.ExecuteScalarAsync(cancellationToken);

        if (machineIdValue is null)
        {
            await using var insertMachine = connection.CreateCommand();
            insertMachine.CommandText = """
                INSERT INTO machine_codes (code, description, is_active, created_at_utc)
                VALUES (@code, @description, 1, UTC_TIMESTAMP(6));
                """;
            insertMachine.Parameters.AddWithValue("@code", _bootstrapAdmin.MachineCode.Trim());
            insertMachine.Parameters.AddWithValue("@description", _bootstrapAdmin.MachineDescription.Trim());
            await insertMachine.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

