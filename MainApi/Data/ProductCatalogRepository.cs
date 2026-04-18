using System.Globalization;
using MainApi.Domain;
using MySqlConnector;

namespace MainApi.Data;

public sealed class ProductCatalogRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public ProductCatalogRepository(MySqlConnectionFactory connectionFactory)
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

    public async Task<PagedQueryResult<ProductCatalogEntryRecord>> QueryAsync(ProductCatalogQuery query, CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(query);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var (whereSql, parameters) = BuildWhereClause(normalizedQuery);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(1) FROM product_catalog_entries p{whereSql};";
        foreach (var parameter in parameters)
        {
            countCommand.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }

        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
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
            FROM product_catalog_entries p
            {whereSql}
            ORDER BY sort_order ASC, id ASC
            LIMIT @limit OFFSET @offset;
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }
        command.Parameters.AddWithValue("@limit", normalizedQuery.PageSize);
        command.Parameters.AddWithValue("@offset", (normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ProductCatalogEntryRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Map(reader));
        }

        return new PagedQueryResult<ProductCatalogEntryRecord>
        {
            TotalCount = totalCount,
            PageNumber = normalizedQuery.PageNumber,
            PageSize = normalizedQuery.PageSize,
            Items = items
        };
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
            insertCommand.Parameters.AddWithValue("@productCode", entry.ProductCode);
            insertCommand.Parameters.AddWithValue("@productName", entry.ProductName);
            insertCommand.Parameters.AddWithValue("@specCode", entry.SpecCode);
            insertCommand.Parameters.AddWithValue("@barcode", entry.Barcode);
            insertCommand.Parameters.AddWithValue("@baseName", entry.BaseName);
            insertCommand.Parameters.AddWithValue("@specificationToken", entry.SpecificationToken);
            insertCommand.Parameters.AddWithValue("@modelToken", entry.ModelToken);
            insertCommand.Parameters.AddWithValue("@degree", entry.Degree);
            insertCommand.Parameters.AddWithValue("@searchText", entry.SearchText);
            insertCommand.Parameters.AddWithValue("@sortOrder", entry.SortOrder);
            insertCommand.Parameters.AddWithValue("@createdAtUtc", FormatDate(entry.UpdatedAtUtc));
            insertCommand.Parameters.AddWithValue("@updatedAtUtc", FormatDate(entry.UpdatedAtUtc));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static ProductCatalogEntryRecord Map(MySqlDataReader reader)
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
            UpdatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "updated_at_utc")
        };
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static ProductCatalogQuery NormalizeQuery(ProductCatalogQuery query)
    {
        return new ProductCatalogQuery
        {
            PageNumber = Math.Max(1, query.PageNumber),
            PageSize = Math.Clamp(query.PageSize, 1, 500),
            Keyword = query.Keyword.Trim(),
            ProductCode = query.ProductCode.Trim(),
            ProductName = query.ProductName.Trim(),
            ModelToken = query.ModelToken.Trim(),
            SpecificationToken = query.SpecificationToken.Trim(),
            Degree = query.Degree.Trim()
        };
    }

    private static (string WhereSql, Dictionary<string, object> Parameters) BuildWhereClause(ProductCatalogQuery query)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            clauses.Add("""
                (
                    p.product_code LIKE @keyword OR
                    p.product_name LIKE @keyword OR
                    p.spec_code LIKE @keyword OR
                    p.barcode LIKE @keyword OR
                    p.base_name LIKE @keyword OR
                    p.specification_token LIKE @keyword OR
                    p.model_token LIKE @keyword OR
                    p.degree LIKE @keyword OR
                    p.search_text LIKE @keyword
                )
                """);
            parameters["@keyword"] = $"%{query.Keyword}%";
        }

        if (!string.IsNullOrWhiteSpace(query.ProductCode))
        {
            clauses.Add("p.product_code LIKE @productCode");
            parameters["@productCode"] = $"%{query.ProductCode}%";
        }

        if (!string.IsNullOrWhiteSpace(query.ProductName))
        {
            clauses.Add("p.product_name LIKE @productName");
            parameters["@productName"] = $"%{query.ProductName}%";
        }

        if (!string.IsNullOrWhiteSpace(query.ModelToken))
        {
            clauses.Add("p.model_token LIKE @modelToken");
            parameters["@modelToken"] = $"%{query.ModelToken}%";
        }

        if (!string.IsNullOrWhiteSpace(query.SpecificationToken))
        {
            clauses.Add("p.specification_token LIKE @specificationToken");
            parameters["@specificationToken"] = $"%{query.SpecificationToken}%";
        }

        if (!string.IsNullOrWhiteSpace(query.Degree))
        {
            clauses.Add("p.degree = @degree");
            parameters["@degree"] = query.Degree;
        }

        return clauses.Count == 0
            ? (string.Empty, parameters)
            : ($" WHERE {string.Join(" AND ", clauses)}", parameters);
    }
}



