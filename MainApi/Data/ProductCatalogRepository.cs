using System.Globalization;
using System.Text.RegularExpressions;
using MainApi.Contracts;
using MainApi.Domain;
using MySqlConnector;

namespace MainApi.Data;

public sealed class ProductCatalogRepository
{
    private const string SortBySpecificationToken = "specificationToken";
    private const string SortByModelToken = "modelToken";
    private const string SortByUpdatedAtUtc = "updatedAtUtc";
    private static readonly Regex TrailingDegreeRegex = new(@"(?<base>.*?)(?<degree>\d{1,4})$", RegexOptions.Compiled);
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
                   pricing_specification_token,
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

    public async Task<DateTime?> GetLastUpdatedAtUtcAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MAX(updated_at_utc)
            FROM product_catalog_entries;
            """;

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value == DBNull.Value)
        {
            return null;
        }

        return value switch
        {
            DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            string text when DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed) => parsed,
            _ => null
        };
    }

    public async Task<int> SyncMissingPricingSpecificationTokensAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE product_catalog_entries
            SET pricing_specification_token = specification_token
            WHERE (pricing_specification_token = '' OR pricing_specification_token IS NULL)
              AND specification_token <> ''
              AND specification_token IS NOT NULL;
            """;
        return await command.ExecuteNonQueryAsync(cancellationToken);
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
                   pricing_specification_token,
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
                    PricingSpecificationToken = GetEffectivePricingSpecificationToken(item),
                    Degree = item.Degree,
                    IsOutOfStock = item.IsOutOfStock,
                    UpdatedAtUtc = item.UpdatedAtUtc
                }).ToList();

                var (specificationToken, modelToken) = SplitGroupKey(group.Key);
                return new ProductCatalogGroupRecord
                {
                    SpecificationToken = specificationToken,
                    PricingSpecificationToken = GetEffectivePricingSpecificationToken(group.First()),
                    ModelToken = modelToken,
                    ItemCount = groupItems.Count,
                    UpdatedAtUtc = groupItems.Max(item => item.UpdatedAtUtc),
                    Degrees = degrees
                };
            })
            .ToList();

        var orderedGroups = ApplyGroupedOrdering(grouped, normalizedQuery);

        var pagedItems = orderedGroups
            .Skip((normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize)
            .Take(normalizedQuery.PageSize)
            .ToList();

        return new PagedQueryResult<ProductCatalogGroupRecord>
        {
            TotalCount = orderedGroups.Count,
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
                var (_, modelToken) = SplitGroupKey(group.Key);
                var pricingSpecificationToken = GetEffectivePricingSpecificationToken(group.First());
                return new ProductCatalogPriceRuleOptionRecord
                {
                    SpecificationToken = pricingSpecificationToken,
                    ModelToken = modelToken,
                    PriceName = BuildPriceRuleName(pricingSpecificationToken, modelToken),
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

    public async Task<IReadOnlyList<string>> ListPricingSpecificationOptionsAsync(CancellationToken cancellationToken = default)
    {
        var allItems = await QueryAllFilteredAsync(new ProductCatalogQuery(), cancellationToken);
        return allItems
            .Select(GetEffectivePricingSpecificationToken)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ProductCatalogImportResult> ImportAsync(
        IReadOnlyList<ProductCatalogEntryRecord> entries,
        string importMode,
        CancellationToken cancellationToken = default)
    {
        var normalizedEntries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ProductCode))
            .GroupBy(entry => entry.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        if (normalizedEntries.Count == 0)
        {
            return new ProductCatalogImportResult(0, 0, 0, await CountAsync(cancellationToken));
        }

        var normalizedImportMode = NormalizeImportMode(importMode);
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existingSnapshotByCode = await LoadExistingByCodeAsync(connection, transaction, cancellationToken);
        var existingSnapshotByBaseCode = BuildExistingByBaseCode(existingSnapshotByCode);
        HydrateMissingFieldsFromExisting(normalizedEntries, existingSnapshotByCode, existingSnapshotByBaseCode);

        if (normalizedImportMode == ProductCatalogImportModes.ClearAndImport)
        {
            await using var clearCommand = connection.CreateCommand();
            clearCommand.Transaction = transaction;
            clearCommand.CommandText = "DELETE FROM product_catalog_entries;";
            await clearCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        else if (normalizedImportMode == ProductCatalogImportModes.Overwrite)
        {
            await DeleteExistingGroupsAsync(connection, transaction, normalizedEntries, cancellationToken);
        }

        var existingByCode = await LoadExistingByCodeAsync(connection, transaction, cancellationToken);
        var nextSortOrder = existingByCode.Values.Count == 0
            ? 0
            : existingByCode.Values.Max(item => item.SortOrder) + 1;

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

            entry.SortOrder = ResolveSortOrder(entry, existingByCode.Values, nextSortOrder + toInsert.Count);
            toInsert.Add(entry);
            existingByCode[productCode] = entry;
        }

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
                    pricing_specification_token = @pricingSpecificationToken,
                    model_token = @modelToken,
                    degree = @degree,
                    is_out_of_stock = @isOutOfStock,
                    search_text = @searchText,
                    updated_at_utc = @updatedAtUtc
                WHERE id = @id;
                """;
            updateCommand.Parameters.AddWithValue("@id", entry.Id);
            ApplyEntryParameters(updateCommand, entry);
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
                    pricing_specification_token,
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
                    @pricingSpecificationToken,
                    @modelToken,
                    @degree,
                    @isOutOfStock,
                    @searchText,
                    @sortOrder,
                    @createdAtUtc,
                    @updatedAtUtc
                );
                """;
            ApplyEntryParameters(insertCommand, entry);
            insertCommand.Parameters.AddWithValue("@sortOrder", entry.SortOrder);
            insertCommand.Parameters.AddWithValue("@createdAtUtc", FormatDate(entry.UpdatedAtUtc));
            insertCommand.Parameters.AddWithValue("@updatedAtUtc", FormatDate(entry.UpdatedAtUtc));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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

    public async Task<bool> UpdateGroupSpecificationTokenAsync(
        string specificationToken,
        string modelToken,
        string targetSpecificationToken,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedSpecificationToken = Safe(specificationToken);
        var normalizedModelToken = Safe(modelToken);
        var normalizedTargetSpecificationToken = Safe(targetSpecificationToken);
        if (string.IsNullOrWhiteSpace(normalizedModelToken) ||
            string.IsNullOrWhiteSpace(normalizedTargetSpecificationToken))
        {
            return false;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE product_catalog_entries
            SET specification_token = @targetSpecificationToken,
                pricing_specification_token = CASE
                    WHEN pricing_specification_token = '' OR pricing_specification_token IS NULL OR pricing_specification_token = specification_token
                        THEN @targetSpecificationToken
                    ELSE pricing_specification_token
                END,
                search_text = LOWER(REPLACE(CONCAT(product_code, ' ', product_name, ' ', @targetSpecificationToken, ' ', model_token, ' ', degree, ' ', barcode), ' ', '')),
                updated_at_utc = @updatedAtUtc
            WHERE ((@specificationToken = '' AND (specification_token = '' OR specification_token IS NULL)) OR specification_token = @specificationToken)
              AND model_token = @modelToken;
            """;
        command.Parameters.AddWithValue("@specificationToken", normalizedSpecificationToken);
        command.Parameters.AddWithValue("@modelToken", normalizedModelToken);
        command.Parameters.AddWithValue("@targetSpecificationToken", normalizedTargetSpecificationToken);
        command.Parameters.AddWithValue("@updatedAtUtc", FormatDate(updatedAtUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> UpdateGroupPricingSpecificationTokenAsync(
        string specificationToken,
        string modelToken,
        string targetPricingSpecificationToken,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedSpecificationToken = Safe(specificationToken);
        var normalizedModelToken = Safe(modelToken);
        var normalizedTargetPricingSpecificationToken = Safe(targetPricingSpecificationToken);
        if (string.IsNullOrWhiteSpace(normalizedModelToken) ||
            string.IsNullOrWhiteSpace(normalizedTargetPricingSpecificationToken))
        {
            return false;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE product_catalog_entries
            SET pricing_specification_token = @targetPricingSpecificationToken,
                updated_at_utc = @updatedAtUtc
            WHERE ((@specificationToken = '' AND (specification_token = '' OR specification_token IS NULL)) OR specification_token = @specificationToken)
              AND model_token = @modelToken;
            """;
        command.Parameters.AddWithValue("@specificationToken", normalizedSpecificationToken);
        command.Parameters.AddWithValue("@modelToken", normalizedModelToken);
        command.Parameters.AddWithValue("@targetPricingSpecificationToken", normalizedTargetPricingSpecificationToken);
        command.Parameters.AddWithValue("@updatedAtUtc", FormatDate(updatedAtUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteGroupAsync(
        string specificationToken,
        string modelToken,
        CancellationToken cancellationToken = default)
    {
        var normalizedSpecificationToken = Safe(specificationToken);
        var normalizedModelToken = Safe(modelToken);
        if (string.IsNullOrWhiteSpace(normalizedModelToken))
        {
            return false;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM product_catalog_entries
            WHERE ((@specificationToken = '' AND (specification_token = '' OR specification_token IS NULL)) OR specification_token = @specificationToken)
              AND model_token = @modelToken;
            """;
        command.Parameters.AddWithValue("@specificationToken", normalizedSpecificationToken);
        command.Parameters.AddWithValue("@modelToken", normalizedModelToken);
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
                PricingSpecificationToken = group.First().PricingSpecificationToken.Trim(),
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
                    pricing_specification_token,
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
                    @pricingSpecificationToken,
                    @modelToken,
                    @degree,
                    @isOutOfStock,
                    @searchText,
                    @sortOrder,
                    @createdAtUtc,
                    @updatedAtUtc
                );
                """;
            ApplyEntryParameters(insertCommand, entry);
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
            PricingSpecificationToken = reader.GetString(reader.GetOrdinal("pricing_specification_token")),
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
            PricingSpecificationToken = query.PricingSpecificationToken.Trim(),
            Degree = query.Degree.Trim(),
            SortBy = NormalizeGroupedSortBy(query.SortBy),
            SortDirection = NormalizeSortDirection(query.SortDirection)
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
                   pricing_specification_token,
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

        if (!string.IsNullOrWhiteSpace(query.PricingSpecificationToken))
        {
            clauses.Add("p.pricing_specification_token LIKE @pricingSpecificationToken");
            parameters["@pricingSpecificationToken"] = $"%{query.PricingSpecificationToken}%";
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

    private static void ApplyEntryParameters(MySqlCommand command, ProductCatalogEntryRecord entry)
    {
        command.Parameters.AddWithValue("@productCode", entry.ProductCode);
        command.Parameters.AddWithValue("@productName", entry.ProductName);
        command.Parameters.AddWithValue("@specCode", entry.SpecCode);
        command.Parameters.AddWithValue("@barcode", entry.Barcode);
        command.Parameters.AddWithValue("@baseName", entry.BaseName);
        command.Parameters.AddWithValue("@specificationToken", entry.SpecificationToken);
        // command.Parameters.AddWithValue("@pricingSpecificationToken", string.IsNullOrWhiteSpace(entry.PricingSpecificationToken) ? entry.SpecificationToken : entry.PricingSpecificationToken);
        command.Parameters.AddWithValue("@pricingSpecificationToken", entry.PricingSpecificationToken);
        command.Parameters.AddWithValue("@modelToken", entry.ModelToken);
        command.Parameters.AddWithValue("@degree", entry.Degree);
        command.Parameters.AddWithValue("@isOutOfStock", entry.IsOutOfStock ? 1 : 0);
        command.Parameters.AddWithValue("@searchText", entry.SearchText);
    }

    private static string NormalizeImportMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() ?? ProductCatalogImportModes.Incremental;
    }

    private static int ResolveSortOrder(ProductCatalogEntryRecord entry, IEnumerable<ProductCatalogEntryRecord> existingEntries, int fallbackSortOrder)
    {
        var matchedGroup = existingEntries.FirstOrDefault(existing =>
            string.Equals(Safe(existing.SpecificationToken), Safe(entry.SpecificationToken), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Safe(existing.ModelToken), Safe(entry.ModelToken), StringComparison.OrdinalIgnoreCase));

        return matchedGroup?.SortOrder ?? fallbackSortOrder;
    }

    private static async Task<Dictionary<string, ProductCatalogEntryRecord>> LoadExistingByCodeAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var existingByCode = new Dictionary<string, ProductCatalogEntryRecord>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,
                   product_code,
                   product_name,
                   spec_code,
                   barcode,
                   base_name,
                   specification_token,
                   pricing_specification_token,
                   model_token,
                   degree,
                   is_out_of_stock,
                   search_text,
                   sort_order,
                   updated_at_utc
            FROM product_catalog_entries;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var existing = Map(reader);
            existingByCode[existing.ProductCode.Trim()] = existing;
        }

        return existingByCode;
    }

    private static async Task DeleteExistingGroupsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IReadOnlyList<ProductCatalogEntryRecord> entries,
        CancellationToken cancellationToken)
    {
        var groups = entries
            .Select(entry => new
            {
                SpecificationToken = Safe(entry.SpecificationToken),
                ModelToken = Safe(entry.ModelToken)
            })
            .Where(group => !string.IsNullOrWhiteSpace(group.ModelToken))
            .Distinct()
            .ToArray();

        if (groups.Length == 0)
        {
            return;
        }

        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;

        var clauses = new List<string>(groups.Length);
        for (var index = 0; index < groups.Length; index++)
        {
            var specParameter = $"@specificationToken{index}";
            var modelParameter = $"@modelToken{index}";
            clauses.Add($"((({specParameter} = '') AND (specification_token = '' OR specification_token IS NULL)) OR specification_token = {specParameter}) AND model_token = {modelParameter}");
            deleteCommand.Parameters.AddWithValue(specParameter, groups[index].SpecificationToken);
            deleteCommand.Parameters.AddWithValue(modelParameter, groups[index].ModelToken);
        }

        deleteCommand.CommandText = $"""
            DELETE FROM product_catalog_entries
            WHERE {string.Join(" OR ", clauses)};
            """;
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<string, ProductCatalogEntryRecord> BuildExistingByBaseCode(
        IReadOnlyDictionary<string, ProductCatalogEntryRecord> existingByCode)
    {
        return existingByCode.Values
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
            .GroupBy(item => NormalizeBaseProductCode(item.ProductCode), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.SpecificationToken))
                    .ThenByDescending(item => !string.IsNullOrWhiteSpace(item.ModelToken))
                    .ThenBy(item => item.SortOrder)
                    .ThenBy(item => item.Id)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static void HydrateMissingFieldsFromExisting(
        IEnumerable<ProductCatalogEntryRecord> entries,
        IReadOnlyDictionary<string, ProductCatalogEntryRecord> existingByCode,
        IReadOnlyDictionary<string, ProductCatalogEntryRecord> existingByBaseCode)
    {
        foreach (var entry in entries)
        {
            if (!existingByCode.TryGetValue(entry.ProductCode.Trim(), out var existing))
            {
                var baseProductCode = NormalizeBaseProductCode(entry.ProductCode);
                if (string.IsNullOrWhiteSpace(baseProductCode) ||
                    !existingByBaseCode.TryGetValue(baseProductCode, out existing))
                {
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(entry.SpecificationToken))
            {
                entry.SpecificationToken = existing.SpecificationToken;
            }

            if (string.IsNullOrWhiteSpace(entry.PricingSpecificationToken))
            {
                entry.PricingSpecificationToken = string.IsNullOrWhiteSpace(existing.PricingSpecificationToken)
                    ? existing.SpecificationToken
                    : existing.PricingSpecificationToken;
            }

            if (string.IsNullOrWhiteSpace(entry.ModelToken))
            {
                entry.ModelToken = existing.ModelToken;
            }

            if (string.IsNullOrWhiteSpace(entry.BaseName))
            {
                entry.BaseName = existing.BaseName;
            }

            if (string.IsNullOrWhiteSpace(entry.ProductName))
            {
                entry.ProductName = existing.ProductName;
            }

            if (string.IsNullOrWhiteSpace(entry.SpecCode))
            {
                entry.SpecCode = existing.SpecCode;
            }

            if (string.IsNullOrWhiteSpace(entry.PricingSpecificationToken))
            {
                entry.PricingSpecificationToken = entry.SpecificationToken;
            }
        }
    }

    private static string NormalizeBaseProductCode(string? productCode)
    {
        var normalized = Safe(productCode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var match = TrailingDegreeRegex.Match(normalized);
        return match.Success ? Safe(match.Groups["base"].Value) : normalized;
    }

    private static string NormalizeGroupedSortBy(string? sortBy)
    {
        return sortBy?.Trim() switch
        {
            SortBySpecificationToken => SortBySpecificationToken,
            SortByModelToken => SortByModelToken,
            _ => SortByUpdatedAtUtc
        };
    }

    private static string NormalizeSortDirection(string? sortDirection)
    {
        return string.Equals(sortDirection?.Trim(), "asc", StringComparison.OrdinalIgnoreCase)
            ? "asc"
            : "desc";
    }

    private static string GetEffectivePricingSpecificationToken(ProductCatalogEntryRecord item)
    {
        var pricingSpecificationToken = Safe(item.PricingSpecificationToken);
        return string.IsNullOrWhiteSpace(pricingSpecificationToken)
            ? Safe(item.SpecificationToken)
            : pricingSpecificationToken;
    }

    private static List<ProductCatalogGroupRecord> ApplyGroupedOrdering(
        IEnumerable<ProductCatalogGroupRecord> groups,
        ProductCatalogQuery query)
    {
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return query.SortBy switch
        {
            SortBySpecificationToken => descending
                ? groups.OrderByDescending(item => item.SpecificationToken, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(item => item.ModelToken, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(item => item.UpdatedAtUtc)
                    .ToList()
                : groups.OrderBy(item => item.SpecificationToken, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.ModelToken, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(item => item.UpdatedAtUtc)
                    .ToList(),
            SortByModelToken => descending
                ? groups.OrderByDescending(item => item.ModelToken, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(item => item.SpecificationToken, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(item => item.UpdatedAtUtc)
                    .ToList()
                : groups.OrderBy(item => item.ModelToken, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.SpecificationToken, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(item => item.UpdatedAtUtc)
                    .ToList(),
            _ => descending
                ? groups.OrderByDescending(item => item.UpdatedAtUtc)
                    .ThenBy(item => item.SpecificationToken, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.ModelToken, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : groups.OrderBy(item => item.UpdatedAtUtc)
                    .ThenBy(item => item.SpecificationToken, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.ModelToken, StringComparer.OrdinalIgnoreCase)
                    .ToList()
        };
    }
}

public sealed record ProductCatalogImportResult(int AddedCount, int UpdatedCount, int SkippedCount, int TotalCount);
