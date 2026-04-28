using MainApi.Domain;
using MySqlConnector;

namespace MainApi.Data;

public sealed class PriceAlertKeywordRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public PriceAlertKeywordRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PriceAlertKeywordRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, keyword, is_active, created_at_utc, updated_at_utc
            FROM order_price_alert_keywords
            ORDER BY is_active DESC, updated_at_utc DESC, id DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<PriceAlertKeywordRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public async Task<PriceAlertKeywordRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, keyword, is_active, created_at_utc, updated_at_utc
            FROM order_price_alert_keywords
            WHERE id = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<PriceAlertKeywordRecord?> FindByKeywordAsync(string keyword, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, keyword, is_active, created_at_utc, updated_at_utc
            FROM order_price_alert_keywords
            WHERE keyword = @keyword
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@keyword", keyword.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<long> CreateAsync(string keyword, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO order_price_alert_keywords (keyword, is_active, created_at_utc, updated_at_utc)
            VALUES (@keyword, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
            """;
        command.Parameters.AddWithValue("@keyword", keyword.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return command.LastInsertedId;
    }

    public async Task UpdateAsync(long id, string keyword, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE order_price_alert_keywords
            SET keyword = @keyword,
                is_active = @isActive,
                updated_at_utc = UTC_TIMESTAMP(6)
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@keyword", keyword.Trim());
        command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM order_price_alert_keywords WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static PriceAlertKeywordRecord Map(MySqlDataReader reader)
    {
        return new PriceAlertKeywordRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Keyword = reader.GetString(reader.GetOrdinal("keyword")),
            IsActive = reader.GetInt64(reader.GetOrdinal("is_active")) == 1,
            CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc"),
            UpdatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "updated_at_utc")
        };
    }
}
