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
                   is_out_of_stock,
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
                   is_out_of_stock,
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

    public async Task<PagedQueryResult<ProductCatalogGroupRecord>> QueryGroupedAsync(ProductCatalogQuery query, CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(query);
        var allItems = await QueryAllFilteredAsync(normalizedQuery, cancellationToken);

        var grouped = allItems
            .GroupBy(item => BuildGroupKey(item), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var groupItems = group
                    .OrderBy(item => SortDegree(item.Degree))
                    .ThenBy(item => item.SortOrder)
                    .ThenBy(item => item.Id)
                    .ToList();

                var degrees = groupItems.Select(item => new ProductCatalogDegreeRecord
                {
                    Id = item.Id,
                    ProductCode = item.ProductCode,
                    ProductName = item.ProductName,
                    SpecCode = item.SpecCode,
                    Barcode = item.Barcode,
                    Degree = item.Degree,
                    IsOutOfStock = item.IsOutOfStock,
                    UpdatedAtUtc = item.UpdatedAtUtc
                }).ToList();

                var (specificationToken, modelToken) = SplitGroupKey(group.Key);
                return new ProductCatalogGroupRecord
                {
                    SpecificationToken = specificationToken,
                    ModelToken = modelToken,
                    ItemCount = groupItems.Count,
                    UpdatedAtUtc = groupItems.Max(item => item.UpdatedAtUtc),
                    Degrees = degrees
                };
            })
            .OrderBy(item => item.SpecificationToken, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ModelToken, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pagedItems = grouped
            .Skip((normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize)
            .Take(normalizedQuery.PageSize)
            .ToList();

        return new PagedQueryResult<ProductCatalogGroupRecord>
        {
            TotalCount = grouped.Count,
            PageNumber = normalizedQuery.PageNumber,
            PageSize = normalizedQuery.PageSize,
            Items = pagedItems
        };
    }

    public async Task<IReadOnlyList<ProductCatalogPriceRuleOptionRecord>> ListPriceRuleOptionsAsync(CancellationToken cancellationToken = default)
    {
        var allItems = await QueryAllFilteredAsync(new ProductCatalogQuery(), cancellationToken);
        return allItems
            .GroupBy(item => BuildGroupKey(item), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var (specificationToken, modelToken) = SplitGroupKey(group.Key);
                return new ProductCatalogPriceRuleOptionRecord
                {
                    SpecificationToken = specificationToken,
                    ModelToken = modelToken,
                    PriceName = BuildPriceRuleName(specificationToken, modelToken),
                    ProductCount = group.Count(),
                    UpdatedAtUtc = group.Max(item => item.UpdatedAtUtc)
                };
            })
            .Where(option =>
                !string.IsNullOrWhiteSpace(option.SpecificationToken) &&
                !string.IsNullOrWhiteSpace(option.ModelToken))
            .OrderBy(option => option.SpecificationToken, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.ModelToken, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ProductCatalogImportResult> ImportAsync(IReadOnlyList<ProductCatalogEntryRecord> entries, CancellationToken cancellationToken = default)
    {
        var normalizedEntries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ProductCode))
            .ToList();
        if (normalizedEntries.Count == 0)
        {
            return new ProductCatalogImportResult(0, 0, 0, await CountAsync(cancellationToken));
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        var existingByCode = new Dictionary<string, ProductCatalogEntryRecord>(StringComparer.OrdinalIgnoreCase);
        var nextSortOrder = 0;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id,
                       product_code,
                       barcode,
                       sort_order,
                       is_out_of_stock
                FROM product_catalog_entries;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var productCode = reader.GetString(reader.GetOrdinal("product_code")).Trim();
                var existing = new ProductCatalogEntryRecord
                {
                    Id = reader.GetInt64(reader.GetOrdinal("id")),
                    ProductCode = productCode,
                    Barcode = reader.GetString(reader.GetOrdinal("barcode")).Trim(),
                    SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
                    IsOutOfStock = reader.GetBoolean(reader.GetOrdinal("is_out_of_stock"))
                };
                existingByCode[productCode] = existing;

                nextSortOrder = Math.Max(nextSortOrder, existing.SortOrder + 1);
            }
        }

        var toInsert = new List<ProductCatalogEntryRecord>();
        var toUpdate = new List<ProductCatalogEntryRecord>();
        var skippedCount = 0;
        var updatedCount = 0;

        foreach (var entry in normalizedEntries)
        {
            var productCode = entry.ProductCode.Trim();

            if (string.IsNullOrWhiteSpace(productCode))
            {
                skippedCount += 1;
                continue;
            }

            if (existingByCode.TryGetValue(productCode, out var existing))
            {
                entry.Id = existing.Id;
                entry.SortOrder = existing.SortOrder;
                toUpdate.Add(entry);
                updatedCount += 1;
                continue;
            }

            entry.SortOrder = nextSortOrder + toInsert.Count;
            toInsert.Add(entry);
            existingByCode[productCode] = entry;
        }

        if (toInsert.Count > 0 || toUpdate.Count > 0)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            foreach (var entry in toUpdate)
            {
                await using var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = """
                    UPDATE product_catalog_entries
                    SET product_code = @productCode,
                        product_name = @productName,
                        spec_code = @specCode,
                        barcode = @barcode,
                        base_name = @baseName,
                        specification_token = @specificationToken,
                        model_token = @modelToken,
                        degree = @degree,
                        is_out_of_stock = @isOutOfStock,
                        search_text = @searchText,
                        updated_at_utc = @updatedAtUtc
                    WHERE id = @id;
                    """;
                updateCommand.Parameters.AddWithValue("@id", entry.Id);
                updateCommand.Parameters.AddWithValue("@productCode", entry.ProductCode);
                updateCommand.Parameters.AddWithValue("@productName", entry.ProductName);
                updateCommand.Parameters.AddWithValue("@specCode", entry.SpecCode);
                updateCommand.Parameters.AddWithValue("@barcode", entry.Barcode);
                updateCommand.Parameters.AddWithValue("@baseName", entry.BaseName);
                updateCommand.Parameters.AddWithValue("@specificationToken", entry.SpecificationToken);
                updateCommand.Parameters.AddWithValue("@modelToken", entry.ModelToken);
                updateCommand.Parameters.AddWithValue("@degree", entry.Degree);
                updateCommand.Parameters.AddWithValue("@isOutOfStock", entry.IsOutOfStock ? 1 : 0);
                updateCommand.Parameters.AddWithValue("@searchText", entry.SearchText);
                updateCommand.Parameters.AddWithValue("@updatedAtUtc", FormatDate(entry.UpdatedAtUtc));
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var entry in toInsert)
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
                        is_out_of_stock,
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
                        @isOutOfStock,
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
                insertCommand.Parameters.AddWithValue("@isOutOfStock", entry.IsOutOfStock ? 1 : 0);
                insertCommand.Parameters.AddWithValue("@searchText", entry.SearchText);
                insertCommand.Parameters.AddWithValue("@sortOrder", entry.SortOrder);
                insertCommand.Parameters.AddWithValue("@createdAtUtc", FormatDate(entry.UpdatedAtUtc));
                insertCommand.Parameters.AddWithValue("@updatedAtUtc", FormatDate(entry.UpdatedAtUtc));
                await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return new ProductCatalogImportResult(toInsert.Count, updatedCount, skippedCount, await CountAsync(cancellationToken));
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM product_catalog_entries WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> UpdateOutOfStockAsync(long id, bool isOutOfStock, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE product_catalog_entries
            SET is_out_of_stock = @isOutOfStock,
                updated_at_utc = @updatedAtUtc
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@isOutOfStock", isOutOfStock ? 1 : 0);
        command.Parameters.AddWithValue("@updatedAtUtc", FormatDate(updatedAtUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
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
                IsOutOfStock = group.First().IsOutOfStock,
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
                    is_out_of_stock,
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
                    @isOutOfStock,
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
            insertCommand.Parameters.AddWithValue("@isOutOfStock", entry.IsOutOfStock ? 1 : 0);
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
            IsOutOfStock = reader.GetBoolean(reader.GetOrdinal("is_out_of_stock")),
            SearchText = reader.GetString(reader.GetOrdinal("search_text")),
            SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
            UpdatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "updated_at_utc")
        };
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM product_catalog_entries;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
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

    private async Task<IReadOnlyList<ProductCatalogEntryRecord>> QueryAllFilteredAsync(ProductCatalogQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var (whereSql, parameters) = BuildWhereClause(query);

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
                   is_out_of_stock,
                   search_text,
                   sort_order,
                   updated_at_utc
            FROM product_catalog_entries p
            {whereSql}
            ORDER BY sort_order ASC, id ASC;
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ProductCatalogEntryRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Map(reader));
        }

        return items;
    }

    private static string BuildGroupKey(ProductCatalogEntryRecord item)
    {
        var specificationToken = Safe(item.SpecificationToken);
        if (string.IsNullOrWhiteSpace(specificationToken))
        {
            specificationToken = Safe(item.SpecCode);
        }

        var modelToken = Safe(item.ModelToken);
        if (string.IsNullOrWhiteSpace(modelToken))
        {
            modelToken = Safe(item.BaseName);
        }
        if (string.IsNullOrWhiteSpace(modelToken))
        {
            modelToken = Safe(item.ProductName);
        }
        if (string.IsNullOrWhiteSpace(modelToken))
        {
            modelToken = Safe(item.ProductCode);
        }

        if (!string.IsNullOrWhiteSpace(specificationToken) &&
            modelToken.StartsWith(specificationToken, StringComparison.OrdinalIgnoreCase))
        {
            modelToken = modelToken[specificationToken.Length..].Trim();
        }

        return $"{specificationToken}||{modelToken}";
    }

    private static (string SpecificationToken, string ModelToken) SplitGroupKey(string key)
    {
        var parts = key.Split(new[] { "||" }, 2, StringSplitOptions.None);
        return parts.Length == 2
            ? (Safe(parts[0]), Safe(parts[1]))
            : (Safe(key), string.Empty);
    }

    private static string BuildPriceRuleName(string specificationToken, string modelToken)
    {
        var normalizedSpecificationToken = Safe(specificationToken);
        var normalizedModelToken = Safe(modelToken);
        if (string.IsNullOrWhiteSpace(normalizedSpecificationToken))
        {
            return normalizedModelToken;
        }

        if (string.IsNullOrWhiteSpace(normalizedModelToken))
        {
            return normalizedSpecificationToken;
        }

        return $"{normalizedSpecificationToken} / {normalizedModelToken}";
    }

    private static int SortDegree(string? degree)
    {
        if (int.TryParse(Safe(degree), out var numeric))
        {
            return numeric;
        }

        return int.MaxValue;
    }

    private static string Safe(string? value)
    {
        return value?.Trim() ?? string.Empty;
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

public sealed record ProductCatalogImportResult(int AddedCount, int UpdatedCount, int SkippedCount, int TotalCount);



