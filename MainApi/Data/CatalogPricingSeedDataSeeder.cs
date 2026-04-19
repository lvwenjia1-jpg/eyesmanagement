using System.Globalization;
using MySqlConnector;

namespace MainApi.Data;

public sealed class CatalogPricingSeedDataSeeder
{
    private static readonly SeedCatalogEntry[] SeedCatalogEntries =
    {
        new("日抛2片深空物语蓝紫350", "深空物语蓝紫", "6932364896736", "日抛2片", "350"),
        new("日抛2片深空物语蓝紫450", "深空物语蓝紫", "6932364896774", "日抛2片", "450"),
        new("日抛2片游仙红350", "游仙红", "6923737381423", "日抛2片", "350"),
        new("日抛2片游仙红450", "游仙红", "6923737381461", "日抛2片", "450"),
        new("日抛2片次元梦境Pro紫350", "次元梦境Pro紫", "6942028269325", "日抛2片", "350"),
        new("日抛2片次元梦境Pro紫450", "次元梦境Pro紫", "6942028269363", "日抛2片", "450")
    };

    private static readonly SeedPriceRule[] SeedPriceRules =
    {
        new("深空物语蓝紫", 180),
        new("游仙红", 180),
        new("次元梦境Pro紫", 199),
        new("日抛2片深空物语蓝紫350", 180),
        new("日抛2片深空物语蓝紫450", 180),
        new("日抛2片游仙红350", 180),
        new("日抛2片游仙红450", 180),
        new("日抛2片次元梦境Pro紫350", 199),
        new("日抛2片次元梦境Pro紫450", 199)
    };

    private readonly MySqlConnectionFactory _connectionFactory;

    public CatalogPricingSeedDataSeeder(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await SeedPriceRulesAsync(connection, transaction, cancellationToken);
        await SeedProductCatalogAsync(connection, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task SeedPriceRulesAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        var now = FormatDate(DateTime.UtcNow);
        foreach (var rule in SeedPriceRules)
        {
            if (await ExistsAsync(connection, transaction, "order_price_rules", "price_name", rule.PriceName, cancellationToken))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO order_price_rules (
                    price_name,
                    price_value,
                    is_active,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES (
                    @priceName,
                    @priceValue,
                    1,
                    @createdAtUtc,
                    @updatedAtUtc
                );
                """;
            command.Parameters.AddWithValue("@priceName", rule.PriceName);
            command.Parameters.AddWithValue("@priceValue", rule.PriceValue);
            command.Parameters.AddWithValue("@createdAtUtc", now);
            command.Parameters.AddWithValue("@updatedAtUtc", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task SeedProductCatalogAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        var now = FormatDate(DateTime.UtcNow);
        var sortOrder = await GetNextSortOrderAsync(connection, transaction, cancellationToken);

        foreach (var entry in SeedCatalogEntries)
        {
            if (await ExistsAsync(connection, transaction, "product_catalog_entries", "product_code", entry.ProductCode, cancellationToken))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO product_catalog_entries (
                    product_code,
                    product_name,
                    spec_code,
                    barcode,
                    base_name,
                    specification_token,
                    model_token,
                    degree,
                    search_text,
                    sort_order,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES (
                    @productCode,
                    @productName,
                    @specCode,
                    @barcode,
                    @baseName,
                    @specificationToken,
                    @modelToken,
                    @degree,
                    @searchText,
                    @sortOrder,
                    @createdAtUtc,
                    @updatedAtUtc
                );
                """;
            command.Parameters.AddWithValue("@productCode", entry.ProductCode);
            command.Parameters.AddWithValue("@productName", entry.ProductName);
            command.Parameters.AddWithValue("@specCode", entry.SpecificationToken);
            command.Parameters.AddWithValue("@barcode", entry.Barcode);
            command.Parameters.AddWithValue("@baseName", entry.ProductName);
            command.Parameters.AddWithValue("@specificationToken", entry.SpecificationToken);
            command.Parameters.AddWithValue("@modelToken", "lenspop日抛");
            command.Parameters.AddWithValue("@degree", entry.Degree);
            command.Parameters.AddWithValue("@searchText", BuildSearchText(entry));
            command.Parameters.AddWithValue("@sortOrder", sortOrder++);
            command.Parameters.AddWithValue("@createdAtUtc", now);
            command.Parameters.AddWithValue("@updatedAtUtc", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> ExistsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string tableName,
        string columnName,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(1) FROM {tableName} WHERE {columnName} = @value;";
        command.Parameters.AddWithValue("@value", value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<int> GetNextSortOrderAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM product_catalog_entries;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string BuildSearchText(SeedCatalogEntry entry)
    {
        return string.Join(' ', new[]
        {
            entry.ProductCode,
            entry.ProductName,
            entry.SpecificationToken,
            entry.Degree,
            entry.Barcode,
            "lenspop日抛"
        });
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private sealed record SeedCatalogEntry(
        string ProductCode,
        string ProductName,
        string Barcode,
        string SpecificationToken,
        string Degree);

    private sealed record SeedPriceRule(string PriceName, int PriceValue);
}
