using System.Globalization;
using MainApi.Domain;
using Microsoft.Data.Sqlite;

namespace MainApi.Data;

public sealed class ProductCatalogRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ProductCatalogRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ProductCatalogEntryRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,
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
                   updated_at_utc
            FROM product_catalog_entries
            ORDER BY sort_order ASC, id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ProductCatalogEntryRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public async Task ReplaceAsync(IReadOnlyList<ProductCatalogEntryRecord> entries, CancellationToken cancellationToken = default)
    {
        var normalizedEntries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ProductCode))
            .GroupBy(entry => entry.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => new ProductCatalogEntryRecord
            {
                ProductCode = group.Key,
                ProductName = group.First().ProductName.Trim(),
                SpecCode = group.First().SpecCode.Trim(),
                Barcode = group.First().Barcode.Trim(),
                BaseName = group.First().BaseName.Trim(),
                SpecificationToken = group.First().SpecificationToken.Trim(),
                ModelToken = group.First().ModelToken.Trim(),
                Degree = group.First().Degree.Trim(),
                SearchText = group.First().SearchText.Trim(),
                SortOrder = index,
                UpdatedAtUtc = DateTime.UtcNow
            })
            .ToList();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM product_catalog_entries;";
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var entry in normalizedEntries)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
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
                    $productCode,
                    $productName,
                    $specCode,
                    $barcode,
                    $baseName,
                    $specificationToken,
                    $modelToken,
                    $degree,
                    $searchText,
                    $sortOrder,
                    $createdAtUtc,
                    $updatedAtUtc
                );
                """;
            insertCommand.Parameters.AddWithValue("$productCode", entry.ProductCode);
            insertCommand.Parameters.AddWithValue("$productName", entry.ProductName);
            insertCommand.Parameters.AddWithValue("$specCode", entry.SpecCode);
            insertCommand.Parameters.AddWithValue("$barcode", entry.Barcode);
            insertCommand.Parameters.AddWithValue("$baseName", entry.BaseName);
            insertCommand.Parameters.AddWithValue("$specificationToken", entry.SpecificationToken);
            insertCommand.Parameters.AddWithValue("$modelToken", entry.ModelToken);
            insertCommand.Parameters.AddWithValue("$degree", entry.Degree);
            insertCommand.Parameters.AddWithValue("$searchText", entry.SearchText);
            insertCommand.Parameters.AddWithValue("$sortOrder", entry.SortOrder);
            insertCommand.Parameters.AddWithValue("$createdAtUtc", FormatDate(entry.UpdatedAtUtc));
            insertCommand.Parameters.AddWithValue("$updatedAtUtc", FormatDate(entry.UpdatedAtUtc));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static ProductCatalogEntryRecord Map(SqliteDataReader reader)
    {
        return new ProductCatalogEntryRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            ProductCode = reader.GetString(reader.GetOrdinal("product_code")),
            ProductName = reader.GetString(reader.GetOrdinal("product_name")),
            SpecCode = reader.GetString(reader.GetOrdinal("spec_code")),
            Barcode = reader.GetString(reader.GetOrdinal("barcode")),
            BaseName = reader.GetString(reader.GetOrdinal("base_name")),
            SpecificationToken = reader.GetString(reader.GetOrdinal("specification_token")),
            ModelToken = reader.GetString(reader.GetOrdinal("model_token")),
            Degree = reader.GetString(reader.GetOrdinal("degree")),
            SearchText = reader.GetString(reader.GetOrdinal("search_text")),
            SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
            UpdatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at_utc")), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
        };
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
