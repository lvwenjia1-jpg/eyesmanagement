using MainApi.Domain;
using MySqlConnector;

namespace MainApi.Data;

public sealed class PriceRuleRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public PriceRuleRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedQueryResult<PriceRuleRecord>> QueryAsync(string keyword, bool? isActive, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var normalizedKeyword = keyword.Trim();
        var normalizedPageNumber = Math.Max(1, pageNumber);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 500);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var whereSql = BuildWhereSql(normalizedKeyword, isActive, out var parameters);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(1) FROM order_price_rules r{whereSql};";
        foreach (var parameter in parameters)
        {
            countCommand.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, price_name, price_value, is_active, created_at_utc, updated_at_utc
            FROM order_price_rules r
            {whereSql}
            ORDER BY r.updated_at_utc DESC, r.id DESC
            LIMIT @limit OFFSET @offset;
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }
        command.Parameters.AddWithValue("@limit", normalizedPageSize);
        command.Parameters.AddWithValue("@offset", (normalizedPageNumber - 1) * normalizedPageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<PriceRuleRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Map(reader));
        }

        return new PagedQueryResult<PriceRuleRecord>
        {
            TotalCount = totalCount,
            PageNumber = normalizedPageNumber,
            PageSize = normalizedPageSize,
            Items = items
        };
    }

    public async Task<PriceRuleRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, price_name, price_value, is_active, created_at_utc, updated_at_utc
            FROM order_price_rules
            WHERE id = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<PriceRuleRecord?> FindByNameAsync(string priceName, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, price_name, price_value, is_active, created_at_utc, updated_at_utc
            FROM order_price_rules
            WHERE price_name = @priceName
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@priceName", priceName.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<long> CreateAsync(string priceName, int priceValue, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO order_price_rules (price_name, price_value, is_active, created_at_utc, updated_at_utc)
            VALUES (@priceName, @priceValue, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
            """;
        command.Parameters.AddWithValue("@priceName", priceName.Trim());
        command.Parameters.AddWithValue("@priceValue", priceValue);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return command.LastInsertedId;
    }

    public async Task UpdateAsync(long id, string priceName, int priceValue, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE order_price_rules
            SET price_name = @priceName,
                price_value = @priceValue,
                is_active = @isActive,
                updated_at_utc = UTC_TIMESTAMP(6)
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@priceName", priceName.Trim());
        command.Parameters.AddWithValue("@priceValue", priceValue);
        command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PriceRuleUpsertResult> UpsertManyAsync(IReadOnlyList<PriceRuleUpsertItem> items, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existingByName = await FindByNamesAsync(
            connection,
            transaction,
            items.Select(item => item.PriceName).ToArray(),
            cancellationToken);

        var createdCount = 0;
        var updatedCount = 0;

        foreach (var item in items)
        {
            if (existingByName.TryGetValue(item.PriceName, out var existing))
            {
                await using var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = """
                    UPDATE order_price_rules
                    SET price_name = @priceName,
                        price_value = @priceValue,
                        is_active = @isActive,
                        updated_at_utc = UTC_TIMESTAMP(6)
                    WHERE id = @id;
                    """;
                updateCommand.Parameters.AddWithValue("@id", existing.Id);
                updateCommand.Parameters.AddWithValue("@priceName", item.PriceName);
                updateCommand.Parameters.AddWithValue("@priceValue", item.PriceValue);
                updateCommand.Parameters.AddWithValue("@isActive", item.IsActive ? 1 : 0);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
                updatedCount += 1;
                continue;
            }

            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO order_price_rules (price_name, price_value, is_active, created_at_utc, updated_at_utc)
                VALUES (@priceName, @priceValue, @isActive, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
                """;
            insertCommand.Parameters.AddWithValue("@priceName", item.PriceName);
            insertCommand.Parameters.AddWithValue("@priceValue", item.PriceValue);
            insertCommand.Parameters.AddWithValue("@isActive", item.IsActive ? 1 : 0);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);

            existingByName[item.PriceName] = new PriceRuleRecord
            {
                Id = insertCommand.LastInsertedId,
                PriceName = item.PriceName,
                PriceValue = item.PriceValue,
                IsActive = item.IsActive
            };
            createdCount += 1;
        }

        await transaction.CommitAsync(cancellationToken);

        return new PriceRuleUpsertResult
        {
            TotalCount = items.Count,
            CreatedCount = createdCount,
            UpdatedCount = updatedCount
        };
    }

    private static string BuildWhereSql(string keyword, bool? isActive, out Dictionary<string, object> parameters)
    {
        var clauses = new List<string>();
        parameters = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            clauses.Add("r.price_name LIKE @keyword");
            parameters["@keyword"] = $"%{keyword}%";
        }

        if (isActive.HasValue)
        {
            clauses.Add("r.is_active = @isActive");
            parameters["@isActive"] = isActive.Value ? 1 : 0;
        }

        return clauses.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", clauses)}";
    }

    private static async Task<Dictionary<string, PriceRuleRecord>> FindByNamesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IReadOnlyCollection<string> priceNames,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, PriceRuleRecord>(StringComparer.OrdinalIgnoreCase);
        if (priceNames.Count == 0)
        {
            return result;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var placeholders = new List<string>(priceNames.Count);
        var index = 0;
        foreach (var priceName in priceNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var parameterName = $"@priceName{index++}";
            placeholders.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, priceName.Trim());
        }

        command.CommandText = $"""
            SELECT id, price_name, price_value, is_active, created_at_utc, updated_at_utc
            FROM order_price_rules
            WHERE price_name IN ({string.Join(", ", placeholders)});
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = Map(reader);
            result[item.PriceName] = item;
        }

        return result;
    }

    private static PriceRuleRecord Map(MySqlDataReader reader)
    {
        return new PriceRuleRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            PriceName = reader.GetString(reader.GetOrdinal("price_name")),
            PriceValue = reader.GetInt32(reader.GetOrdinal("price_value")),
            IsActive = reader.GetInt64(reader.GetOrdinal("is_active")) == 1,
            CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc"),
            UpdatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "updated_at_utc")
        };
    }
}
