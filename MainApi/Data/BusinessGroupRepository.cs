using System.Globalization;
using MainApi.Domain;
using MySqlConnector;

namespace MainApi.Data;

public sealed class BusinessGroupRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public BusinessGroupRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedQueryResult<BusinessGroupRecord>> QueryAsync(string keyword, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var normalizedKeyword = keyword.Trim();
        var normalizedPageNumber = Math.Max(1, pageNumber);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 200);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var whereSql = string.IsNullOrWhiteSpace(normalizedKeyword) ? string.Empty : " WHERE bg.name LIKE @keyword";

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(1) FROM business_groups bg{whereSql};";
        if (!string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            countCommand.Parameters.AddWithValue("@keyword", $"%{normalizedKeyword}%");
        }
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                bg.id,
                bg.name,
                bg.balance,
                bg.created_at_utc,
                bg.updated_at_utc,
                COALESCE((
                    SELECT COUNT(1)
                    FROM order_uploads o
                    WHERE o.business_group_id = bg.id
                ), 0) AS order_count
            FROM business_groups bg
            {whereSql}
            ORDER BY bg.id ASC
            LIMIT @limit OFFSET @offset;
            """;
        if (!string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            command.Parameters.AddWithValue("@keyword", $"%{normalizedKeyword}%");
        }
        command.Parameters.AddWithValue("@limit", normalizedPageSize);
        command.Parameters.AddWithValue("@offset", (normalizedPageNumber - 1) * normalizedPageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<BusinessGroupRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Map(reader));
        }

        return new PagedQueryResult<BusinessGroupRecord>
        {
            TotalCount = totalCount,
            PageNumber = normalizedPageNumber,
            PageSize = normalizedPageSize,
            Items = items
        };
    }

    public async Task<BusinessGroupRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                bg.id,
                bg.name,
                bg.balance,
                bg.created_at_utc,
                bg.updated_at_utc,
                COALESCE((
                    SELECT COUNT(1)
                    FROM order_uploads o
                    WHERE o.business_group_id = bg.id
                ), 0) AS order_count
            FROM business_groups bg
            WHERE bg.id = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task UpdateBalanceAsync(long id, decimal balance, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE business_groups
            SET balance = @balance,
                updated_at_utc = @updatedAtUtc
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@balance", balance);
        command.Parameters.AddWithValue("@updatedAtUtc", FormatDate(DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static BusinessGroupRecord Map(MySqlDataReader reader)
    {
        return new BusinessGroupRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Balance = reader.GetDecimal(reader.GetOrdinal("balance")),
            OrderCount = reader.GetInt32(reader.GetOrdinal("order_count")),
            CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc"),
            UpdatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "updated_at_utc")
        };
    }

    private static DateTime ParseDate(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}



