using MainApi.Contracts;
using MySqlConnector;

namespace MainApi.Data;

public sealed class QuantityUnitSettingsRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public QuantityUnitSettingsRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<QuantityUnitSettingsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        await using MySqlConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT unit_token, actual_quantity, sort_order FROM quantity_unit_settings ORDER BY sort_order, unit_token;";
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var response = new QuantityUnitSettingsResponse();
        while (await reader.ReadAsync(cancellationToken))
        {
            response.Items.Add(new QuantityUnitSettingItem
            {
                Unit = reader.GetString(0),
                ActualQuantity = reader.GetInt32(1),
                SortOrder = reader.GetInt32(2)
            });
        }

        return response;
    }

    public async Task SaveAsync(IEnumerable<QuantityUnitSettingItem> items, CancellationToken cancellationToken = default)
    {
        await using MySqlConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (MySqlCommand deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM quantity_unit_settings;";
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (QuantityUnitSettingItem item in items)
        {
            await using MySqlCommand insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO quantity_unit_settings (unit_token, actual_quantity, sort_order, created_at_utc, updated_at_utc) VALUES (@unit, @quantity, @sortOrder, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));";
            insertCommand.Parameters.AddWithValue("@unit", item.Unit);
            insertCommand.Parameters.AddWithValue("@quantity", item.ActualQuantity);
            insertCommand.Parameters.AddWithValue("@sortOrder", item.SortOrder);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
