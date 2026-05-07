using MainApi.Contracts;
using MySqlConnector;

namespace MainApi.Data;

public sealed class WearPeriodSettingsRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public WearPeriodSettingsRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<WearPeriodSettingsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return new WearPeriodSettingsResponse
        {
            WearPeriods = await ListWearPeriodsAsync(connection, cancellationToken),
            WearPeriodMappings = await ListAliasesAsync(connection, cancellationToken)
        };
    }

    public async Task SaveAsync(UpdateWearPeriodSettingsRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteAliases = connection.CreateCommand())
        {
            deleteAliases.Transaction = transaction;
            deleteAliases.CommandText = "DELETE FROM wear_period_aliases;";
            await deleteAliases.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deletePeriods = connection.CreateCommand())
        {
            deletePeriods.Transaction = transaction;
            deletePeriods.CommandText = "DELETE FROM wear_period_definitions;";
            await deletePeriods.ExecuteNonQueryAsync(cancellationToken);
        }

        var normalizedPeriods = request.WearPeriods
            .Select(item => new WearPeriodItemResponse
            {
                Value = item.Value?.Trim() ?? string.Empty,
                SortOrder = item.SortOrder
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => new WearPeriodItemResponse
            {
                Value = group.First().Value,
                SortOrder = group.First().SortOrder == 0 ? index : group.First().SortOrder
            })
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var period in normalizedPeriods)
        {
            await using var insertPeriod = connection.CreateCommand();
            insertPeriod.Transaction = transaction;
            insertPeriod.CommandText = """
                INSERT INTO wear_period_definitions (wear_period, sort_order, created_at_utc, updated_at_utc)
                VALUES (@wearPeriod, @sortOrder, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
                """;
            insertPeriod.Parameters.AddWithValue("@wearPeriod", period.Value);
            insertPeriod.Parameters.AddWithValue("@sortOrder", period.SortOrder);
            await insertPeriod.ExecuteNonQueryAsync(cancellationToken);
        }

        var knownPeriods = normalizedPeriods
            .Select(item => item.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedAliases = request.WearPeriodMappings
            .Select(item => new WearPeriodAliasItemResponse
            {
                Alias = item.Alias?.Trim() ?? string.Empty,
                WearPeriod = item.WearPeriod?.Trim() ?? string.Empty,
                SortOrder = item.SortOrder
            })
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Alias) &&
                !string.IsNullOrWhiteSpace(item.WearPeriod) &&
                knownPeriods.Contains(item.WearPeriod))
            .GroupBy(item => $"{item.WearPeriod}||{item.Alias}", StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => new WearPeriodAliasItemResponse
            {
                Alias = group.First().Alias,
                WearPeriod = group.First().WearPeriod,
                SortOrder = group.First().SortOrder == 0 ? index : group.First().SortOrder
            })
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.WearPeriod, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Alias, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var alias in normalizedAliases)
        {
            await using var insertAlias = connection.CreateCommand();
            insertAlias.Transaction = transaction;
            insertAlias.CommandText = """
                INSERT INTO wear_period_aliases (wear_period, alias, sort_order, created_at_utc, updated_at_utc)
                VALUES (@wearPeriod, @alias, @sortOrder, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
                """;
            insertAlias.Parameters.AddWithValue("@wearPeriod", alias.WearPeriod);
            insertAlias.Parameters.AddWithValue("@alias", alias.Alias);
            insertAlias.Parameters.AddWithValue("@sortOrder", alias.SortOrder);
            await insertAlias.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<List<WearPeriodItemResponse>> ListWearPeriodsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT wear_period, sort_order
            FROM wear_period_definitions
            ORDER BY sort_order ASC, wear_period ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<WearPeriodItemResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new WearPeriodItemResponse
            {
                Value = reader.GetString(reader.GetOrdinal("wear_period")),
                SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order"))
            });
        }

        return result;
    }

    private static async Task<List<WearPeriodAliasItemResponse>> ListAliasesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT wear_period, alias, sort_order
            FROM wear_period_aliases
            ORDER BY sort_order ASC, wear_period ASC, alias ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<WearPeriodAliasItemResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new WearPeriodAliasItemResponse
            {
                WearPeriod = reader.GetString(reader.GetOrdinal("wear_period")),
                Alias = reader.GetString(reader.GetOrdinal("alias")),
                SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order"))
            });
        }

        return result;
    }
}
