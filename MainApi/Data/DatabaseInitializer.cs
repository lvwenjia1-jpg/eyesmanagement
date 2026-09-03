using MainApi.Options;
using MainApi.Services;
using MainApi.Domain;
using MySqlConnector;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

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
                erp_id VARCHAR(64) NULL,
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
                raw_text LONGTEXT NOT NULL,
                snapshot_json LONGTEXT NOT NULL,
                amount DECIMAL(18,2) NOT NULL DEFAULT 0,
                tracking_number VARCHAR(128) NOT NULL DEFAULT '',
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
                price_rule_id BIGINT NULL,
                price_name VARCHAR(128) NOT NULL DEFAULT '',
                unit_price INT NOT NULL DEFAULT 0,
                line_amount INT NOT NULL DEFAULT 0,
                KEY idx_order_upload_items_order_upload_id (order_upload_id),
                CONSTRAINT fk_order_upload_items_order_upload_id FOREIGN KEY (order_upload_id) REFERENCES order_uploads(id) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS order_price_rules (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                rule_type VARCHAR(32) NOT NULL DEFAULT 'base',
                price_name VARCHAR(128) NOT NULL,
                specification_token VARCHAR(128) NOT NULL DEFAULT '',
                model_token VARCHAR(128) NOT NULL DEFAULT '',
                required_quantity INT NOT NULL DEFAULT 0,
                price_value INT NOT NULL DEFAULT 0,
                is_active TINYINT(1) NOT NULL DEFAULT 1,
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uq_order_price_rules_price_name (price_name)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS order_price_alert_keywords (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                keyword VARCHAR(64) NOT NULL,
                is_active TINYINT(1) NOT NULL DEFAULT 1,
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uq_order_price_alert_keywords_keyword (keyword)
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
                pricing_specification_token VARCHAR(128) NOT NULL DEFAULT '',
                model_token VARCHAR(128) NOT NULL,
                degree VARCHAR(64) NOT NULL,
                is_out_of_stock TINYINT(1) NOT NULL DEFAULT 0,
                search_text VARCHAR(512) NOT NULL,
                sort_order INT NOT NULL DEFAULT 0,
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uq_product_catalog_entries_product_code (product_code)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS wear_period_definitions (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                wear_period VARCHAR(128) NOT NULL,
                sort_order INT NOT NULL DEFAULT 0,
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uq_wear_period_definitions_wear_period (wear_period)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS wear_period_aliases (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                wear_period VARCHAR(128) NOT NULL,
                alias VARCHAR(128) NOT NULL,
                sort_order INT NOT NULL DEFAULT 0,
                created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY uq_wear_period_aliases_wear_period_alias (wear_period, alias)
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
        await EnsureUserColumnsAsync(connection, cancellationToken);
        await EnsurePriceRuleColumnLengthsAsync(connection, cancellationToken);
        await EnsureIndexesAsync(connection, cancellationToken);
        await MigrateLegacyClearanceRulesAsync(connection, cancellationToken);
        await CleanupLegacyPriceRulesAsync(connection, cancellationToken);
        await BackfillUploadSummaryColumnsAsync(connection, cancellationToken);
        await BackfillUploadPriceColumnsAsync(connection, cancellationToken);
        // await BackfillProductCatalogPricingSpecificationAsync(connection, cancellationToken);
        await NormalizeUploadHistoryAsync(connection, cancellationToken);
        await EnsureWearPeriodDefaultsAsync(connection, cancellationToken);
    }

    private static async Task EnsureUploadColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, "order_uploads", "item_count", "INT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "order_uploads", "created_on", "INT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "order_uploads", "business_group_id", "BIGINT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "order_uploads", "business_group_name", "VARCHAR(128) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "order_uploads", "raw_text", "LONGTEXT NOT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "order_uploads", "snapshot_json", "LONGTEXT NOT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "order_uploads", "amount", "DECIMAL(18,2) NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "order_uploads", "tracking_number", "VARCHAR(128) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "order_upload_items", "price_rule_id", "BIGINT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "order_upload_items", "price_name", "VARCHAR(128) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "order_upload_items", "unit_price", "INT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "order_upload_items", "line_amount", "INT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "order_price_rules", "rule_type", "VARCHAR(32) NOT NULL DEFAULT 'base'", cancellationToken);
        await EnsureColumnAsync(connection, "order_price_rules", "specification_token", "VARCHAR(128) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "order_price_rules", "model_token", "VARCHAR(128) NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "order_price_rules", "required_quantity", "INT NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "product_catalog_entries", "is_out_of_stock", "TINYINT(1) NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "product_catalog_entries", "pricing_specification_token", "VARCHAR(128) NOT NULL DEFAULT ''", cancellationToken);
    }

    private static async Task EnsureUserColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await EnsureNullableVarcharColumnAsync(connection, "users", "erp_id", 64, cancellationToken);

        await using (var normalizeRoles = connection.CreateCommand())
        {
            normalizeRoles.CommandText = """
                UPDATE users
                SET role = LOWER(TRIM(role))
                WHERE role <> LOWER(TRIM(role));
                """;
            await normalizeRoles.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var fixInvalidRoles = connection.CreateCommand())
        {
            fixInvalidRoles.CommandText = """
                UPDATE users
                SET role = 'user'
                WHERE role NOT IN ('user', 'manager', 'admin');
                """;
            await fixInvalidRoles.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var normalizeErpId = connection.CreateCommand();
        normalizeErpId.CommandText = """
            UPDATE users
            SET erp_id = NULL
            WHERE erp_id IS NOT NULL
              AND TRIM(erp_id) = '';
            """;
        await normalizeErpId.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureIndexesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var indexes = new[]
        {
            ("business_groups", "idx_business_groups_name", "CREATE INDEX idx_business_groups_name ON business_groups(name)"),
            ("dashboard_orders", "idx_dashboard_orders_group_created", "CREATE INDEX idx_dashboard_orders_group_created ON dashboard_orders(business_group_id, created_at_utc DESC, id DESC)"),
            ("dashboard_order_items", "idx_dashboard_order_items_order_id", "CREATE INDEX idx_dashboard_order_items_order_id ON dashboard_order_items(order_id)"),
            ("order_uploads", "idx_order_uploads_created_on_id", "CREATE INDEX idx_order_uploads_created_on_id ON order_uploads(created_on DESC, id DESC)"),
            ("order_uploads", "idx_order_uploads_created_at_utc_id", "CREATE INDEX idx_order_uploads_created_at_utc_id ON order_uploads(created_at_utc DESC, id DESC)"),
            ("order_uploads", "idx_order_uploads_machine_created_on_id", "CREATE INDEX idx_order_uploads_machine_created_on_id ON order_uploads(machine_code, created_on DESC, id DESC)"),
            ("order_uploads", "idx_order_uploads_status_created_on_id", "CREATE INDEX idx_order_uploads_status_created_on_id ON order_uploads(status, created_on DESC, id DESC)"),
            ("order_uploads", "idx_order_uploads_uploader_created_on_id", "CREATE INDEX idx_order_uploads_uploader_created_on_id ON order_uploads(uploader_login_name, created_on DESC, id DESC)"),
            ("order_uploads", "idx_order_uploads_uploader_business_group", "CREATE INDEX idx_order_uploads_uploader_business_group ON order_uploads(uploader_login_name, business_group_name)"),
            ("order_uploads", "idx_order_uploads_business_group_name", "CREATE INDEX idx_order_uploads_business_group_name ON order_uploads(business_group_name)"),
            ("order_uploads", "idx_order_uploads_business_group_created_on_id", "CREATE INDEX idx_order_uploads_business_group_created_on_id ON order_uploads(business_group_id, created_on DESC, id DESC)"),
            ("order_uploads", "idx_order_uploads_business_group_created_at_utc_id", "CREATE INDEX idx_order_uploads_business_group_created_at_utc_id ON order_uploads(business_group_id, created_at_utc DESC, id DESC)"),
            ("order_uploads", "idx_order_uploads_business_group_order_status", "CREATE INDEX idx_order_uploads_business_group_order_status ON order_uploads(business_group_id, order_number, status)"),
            ("order_upload_items", "idx_order_upload_items_order_upload_id", "CREATE INDEX idx_order_upload_items_order_upload_id ON order_upload_items(order_upload_id)"),
            ("order_upload_items", "idx_order_upload_items_price_rule_id", "CREATE INDEX idx_order_upload_items_price_rule_id ON order_upload_items(price_rule_id)"),
            ("order_price_rules", "idx_order_price_rules_type_spec_qty", "CREATE INDEX idx_order_price_rules_type_spec_qty ON order_price_rules(rule_type, specification_token, required_quantity)"),
            ("order_price_alert_keywords", "idx_order_price_alert_keywords_active_keyword", "CREATE INDEX idx_order_price_alert_keywords_active_keyword ON order_price_alert_keywords(is_active, keyword)"),
            ("product_catalog_entries", "idx_product_catalog_entries_sort_order_id", "CREATE INDEX idx_product_catalog_entries_sort_order_id ON product_catalog_entries(sort_order ASC, id ASC)"),
            ("wear_period_definitions", "idx_wear_period_definitions_sort_order", "CREATE INDEX idx_wear_period_definitions_sort_order ON wear_period_definitions(sort_order ASC, wear_period ASC)"),
            ("wear_period_aliases", "idx_wear_period_aliases_sort_order", "CREATE INDEX idx_wear_period_aliases_sort_order ON wear_period_aliases(sort_order ASC, wear_period ASC, alias ASC)")
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

    private static async Task EnsureWearPeriodDefaultsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var defaultPeriods = new[]
        {
            "半年抛",
            "年抛",
            "日抛2片",
            "日抛10片",
            "试戴片"
        };

        for (var index = 0; index < defaultPeriods.Length; index++)
        {
            await using var insertPeriod = connection.CreateCommand();
            insertPeriod.CommandText = """
                INSERT INTO wear_period_definitions (wear_period, sort_order, created_at_utc, updated_at_utc)
                VALUES (@wearPeriod, @sortOrder, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    sort_order = VALUES(sort_order),
                    updated_at_utc = UTC_TIMESTAMP(6);
                """;
            insertPeriod.Parameters.AddWithValue("@wearPeriod", defaultPeriods[index]);
            insertPeriod.Parameters.AddWithValue("@sortOrder", index);
            await insertPeriod.ExecuteNonQueryAsync(cancellationToken);
        }

        var defaultAliases = new (string WearPeriod, string Alias)[]
        {
            ("半年抛", "半抛"),
            ("年抛", "年拋"),
            ("日抛2片", "日抛两片"),
            ("日抛2片", "日抛2片装"),
            ("日抛2片", "日抛两片装"),
            ("日抛10片", "日抛十片"),
            ("日抛10片", "日抛10片装"),
            ("日抛10片", "日抛十片装"),
            ("试戴片", "试戴"),
            ("试戴片", "试用")
        };

        for (var index = 0; index < defaultAliases.Length; index++)
        {
            await using var insertAlias = connection.CreateCommand();
            insertAlias.CommandText = """
                INSERT INTO wear_period_aliases (wear_period, alias, sort_order, created_at_utc, updated_at_utc)
                VALUES (@wearPeriod, @alias, @sortOrder, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    sort_order = VALUES(sort_order),
                    updated_at_utc = UTC_TIMESTAMP(6);
                """;
            insertAlias.Parameters.AddWithValue("@wearPeriod", defaultAliases[index].WearPeriod);
            insertAlias.Parameters.AddWithValue("@alias", defaultAliases[index].Alias);
            insertAlias.Parameters.AddWithValue("@sortOrder", index);
            await insertAlias.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task CleanupLegacyPriceRulesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM order_price_rules
            WHERE specification_token = ''
               OR specification_token IS NULL
               OR rule_type NOT IN ('base', 'bulk', 'clearance')
               OR (rule_type = 'clearance' AND (model_token = '' OR model_token IS NULL OR required_quantity <= 0));
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsurePriceRuleColumnLengthsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await EnsureVarcharLengthAtLeastAsync(connection, "order_price_rules", "price_name", 256, cancellationToken);
        await EnsureVarcharLengthAtLeastAsync(connection, "order_price_rules", "model_token", 2048, cancellationToken);
    }

    private static async Task EnsureVarcharLengthAtLeastAsync(
        MySqlConnection connection,
        string tableName,
        string columnName,
        int minLength,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT data_type, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = @tableName
              AND column_name = @columnName
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return;
        }

        var dataType = reader.GetString(reader.GetOrdinal("data_type"));
        var currentLength = reader.IsDBNull(reader.GetOrdinal("character_maximum_length"))
            ? 0
            : reader.GetInt32(reader.GetOrdinal("character_maximum_length"));

        if (!string.Equals(dataType, "varchar", StringComparison.OrdinalIgnoreCase) || currentLength >= minLength)
        {
            return;
        }

        await reader.DisposeAsync();
        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE `{tableName}` MODIFY COLUMN `{columnName}` VARCHAR({minLength}) NOT NULL DEFAULT '';";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MigrateLegacyClearanceRulesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LegacyPriceRuleRow>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, rule_type, specification_token, model_token, required_quantity, price_value, is_active
                FROM order_price_rules
                WHERE rule_type IN ('clearance', 'clearance_threshold')
                ORDER BY specification_token ASC, id ASC;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new LegacyPriceRuleRow
                {
                    Id = reader.GetInt64(reader.GetOrdinal("id")),
                    RuleType = reader.GetString(reader.GetOrdinal("rule_type")),
                    SpecificationToken = reader.GetString(reader.GetOrdinal("specification_token")),
                    ModelToken = reader.GetString(reader.GetOrdinal("model_token")),
                    RequiredQuantity = reader.GetInt32(reader.GetOrdinal("required_quantity")),
                    PriceValue = reader.GetInt32(reader.GetOrdinal("price_value")),
                    IsActive = reader.GetInt64(reader.GetOrdinal("is_active")) == 1
                });
            }
        }

        var specsToCleanup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in rows.GroupBy(row => NormalizePriceRuleText(row.SpecificationToken), StringComparer.OrdinalIgnoreCase))
        {
            var specificationToken = group.Key;
            if (string.IsNullOrWhiteSpace(specificationToken))
            {
                continue;
            }

            var legacyClearanceRows = group
                .Where(row => row.RuleType == PriceRuleTypes.Clearance && row.RequiredQuantity <= 0 && !string.IsNullOrWhiteSpace(row.ModelToken))
                .ToArray();
            var legacyThresholdRows = group
                .Where(row => row.RuleType == "clearance_threshold" && row.RequiredQuantity > 0)
                .OrderByDescending(row => row.RequiredQuantity)
                .ThenBy(row => row.Id)
                .ToArray();
            var mergedRows = group
                .Where(row => row.RuleType == PriceRuleTypes.Clearance && row.RequiredQuantity > 0 && !string.IsNullOrWhiteSpace(row.ModelToken))
                .ToArray();

            if (mergedRows.Length > 0)
            {
                specsToCleanup.Add(specificationToken);
                continue;
            }

            if (legacyClearanceRows.Length == 0 || legacyThresholdRows.Length == 0)
            {
                continue;
            }

            var thresholdRow = legacyThresholdRows[0];
            var modelTokens = legacyClearanceRows
                .SelectMany(row => SplitLegacyModelTokens(row.ModelToken))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(model => model, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), false))
                .ToArray();

            if (modelTokens.Length == 0)
            {
                continue;
            }

            var isActive = thresholdRow.IsActive && legacyClearanceRows.Any(row => row.IsActive);
            var mergedPriceName = BuildClearancePriceName(specificationToken, modelTokens, thresholdRow.RequiredQuantity, thresholdRow.PriceValue);

            await using var upsertCommand = connection.CreateCommand();
            upsertCommand.CommandText = """
                INSERT INTO order_price_rules (
                    rule_type,
                    price_name,
                    specification_token,
                    model_token,
                    required_quantity,
                    price_value,
                    is_active,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES (
                    'clearance',
                    @priceName,
                    @specificationToken,
                    @modelToken,
                    @requiredQuantity,
                    @priceValue,
                    @isActive,
                    UTC_TIMESTAMP(6),
                    UTC_TIMESTAMP(6)
                )
                ON DUPLICATE KEY UPDATE
                    specification_token = VALUES(specification_token),
                    model_token = VALUES(model_token),
                    required_quantity = VALUES(required_quantity),
                    price_value = VALUES(price_value),
                    is_active = VALUES(is_active),
                    updated_at_utc = UTC_TIMESTAMP(6);
                """;
            upsertCommand.Parameters.AddWithValue("@priceName", mergedPriceName);
            upsertCommand.Parameters.AddWithValue("@specificationToken", specificationToken);
            upsertCommand.Parameters.AddWithValue("@modelToken", string.Join("|", modelTokens));
            upsertCommand.Parameters.AddWithValue("@requiredQuantity", thresholdRow.RequiredQuantity);
            upsertCommand.Parameters.AddWithValue("@priceValue", thresholdRow.PriceValue);
            upsertCommand.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
            await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
            specsToCleanup.Add(specificationToken);
        }

        foreach (var specificationToken in specsToCleanup)
        {
            await using var cleanupCommand = connection.CreateCommand();
            cleanupCommand.CommandText = """
                DELETE FROM order_price_rules
                WHERE specification_token = @specificationToken
                  AND (
                        rule_type = 'clearance_threshold'
                        OR (rule_type = 'clearance' AND required_quantity <= 0)
                      );
                """;
            cleanupCommand.Parameters.AddWithValue("@specificationToken", specificationToken);
            await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task EnsureNullableVarcharColumnAsync(
        MySqlConnection connection,
        string tableName,
        string columnName,
        int length,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT is_nullable, data_type, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = @tableName
              AND column_name = @columnName
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return;
        }

        var isNullable = string.Equals(reader.GetString(reader.GetOrdinal("is_nullable")), "YES", StringComparison.OrdinalIgnoreCase);
        var dataType = reader.GetString(reader.GetOrdinal("data_type"));
        var currentLength = reader.IsDBNull(reader.GetOrdinal("character_maximum_length"))
            ? 0
            : reader.GetInt32(reader.GetOrdinal("character_maximum_length"));

        if (isNullable && string.Equals(dataType, "varchar", StringComparison.OrdinalIgnoreCase) && currentLength >= length)
        {
            return;
        }

        await reader.DisposeAsync();
        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE `{tableName}` MODIFY COLUMN `{columnName}` VARCHAR({length}) NULL;";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task BackfillUploadPriceColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var fillPriceName = connection.CreateCommand())
        {
            fillPriceName.CommandText = """
                UPDATE order_upload_items
                SET price_name = product_name
                WHERE price_name = '' OR price_name IS NULL;
                """;
            await fillPriceName.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var fillLineAmount = connection.CreateCommand())
        {
            fillLineAmount.CommandText = """
                UPDATE order_upload_items
                SET line_amount = quantity * unit_price
                WHERE line_amount = 0;
                """;
            await fillLineAmount.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var fillUploadAmount = connection.CreateCommand();
        fillUploadAmount.CommandText = """
            UPDATE order_uploads uploads
            LEFT JOIN (
                SELECT order_upload_id, COALESCE(SUM(line_amount), 0) AS total_amount
                FROM order_upload_items
                GROUP BY order_upload_id
            ) summary ON summary.order_upload_id = uploads.id
            SET uploads.amount = COALESCE(summary.total_amount, 0)
            WHERE uploads.amount = 0;
            """;
        await fillUploadAmount.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task BackfillProductCatalogPricingSpecificationAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE product_catalog_entries
            SET pricing_specification_token = specification_token
            WHERE pricing_specification_token = '' OR pricing_specification_token IS NULL;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task NormalizeUploadHistoryAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        const int cleanupBatchSize = 1000;

        while (true)
        {
            await using var deleteNonFinalStatuses = connection.CreateCommand();
            deleteNonFinalStatuses.CommandTimeout = 180;
            deleteNonFinalStatuses.CommandText = $"""
                DELETE FROM order_uploads
                WHERE status NOT IN ('上传成功', '已取消')
                LIMIT {cleanupBatchSize};
                """;

            var affectedRows = await deleteNonFinalStatuses.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows == 0)
            {
                break;
            }
        }

        while (true)
        {
            await using var deleteDuplicateFinalStatuses = connection.CreateCommand();
            deleteDuplicateFinalStatuses.CommandTimeout = 180;
            deleteDuplicateFinalStatuses.CommandText = $"""
                DELETE FROM order_uploads
                WHERE id IN (
                    SELECT id
                    FROM (
                        SELECT older.id
                        FROM order_uploads older
                        INNER JOIN order_uploads newer
                            ON older.order_number = newer.order_number
                           AND older.status = newer.status
                           AND older.order_number <> ''
                           AND (
                                older.created_at_utc < newer.created_at_utc OR
                                (older.created_at_utc = newer.created_at_utc AND older.id < newer.id)
                           )
                        LIMIT {cleanupBatchSize}
                    ) duplicate_ids
                );
                """;

            var affectedRows = await deleteDuplicateFinalStatuses.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows == 0)
            {
                break;
            }
        }
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
            insertUser.Parameters.AddWithValue("@erpId", string.IsNullOrWhiteSpace(_bootstrapAdmin.ErpId) ? DBNull.Value : _bootstrapAdmin.ErpId.Trim());
            insertUser.Parameters.AddWithValue("@role", UserRoles.Normalize(_bootstrapAdmin.Role) is { Length: > 0 } normalizedRole ? normalizedRole : UserRoles.Manager);
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

    private static string BuildClearancePriceName(string specificationToken, IReadOnlyList<string> modelTokens, int requiredQuantity, int priceValue)
    {
        var payload = $"{specificationToken}|{requiredQuantity}|{priceValue}|{string.Join("|", modelTokens)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..8];
        return $"清仓 / {specificationToken} / {requiredQuantity}副 / {priceValue}元 / {modelTokens.Count}款 / {hash}";
    }

    private static IReadOnlyList<string> SplitLegacyModelTokens(string? modelToken)
    {
        return (modelToken ?? string.Empty)
            .Split(new[] { ',', '，', ';', '；', '、', '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizePriceRuleText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string NormalizePriceRuleText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private sealed class LegacyPriceRuleRow
    {
        public long Id { get; set; }

        public string RuleType { get; set; } = string.Empty;

        public string SpecificationToken { get; set; } = string.Empty;

        public string ModelToken { get; set; } = string.Empty;

        public int RequiredQuantity { get; set; }

        public int PriceValue { get; set; }

        public bool IsActive { get; set; }
    }
}

