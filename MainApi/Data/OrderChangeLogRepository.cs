using System.Globalization;
using MainApi.Domain;
using MySqlConnector;

namespace MainApi.Data;

public sealed class OrderChangeLogRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public OrderChangeLogRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(
        DashboardOrderDetailRecord order,
        string modifierLoginName,
        decimal previousAmount,
        decimal currentAmount,
        string changeSummary,
        CancellationToken cancellationToken = default)
    {
        await CreateAsync(order.Id, order.OrderNo, order.BusinessGroupName, order.ReceiverName, modifierLoginName, previousAmount, currentAmount, changeSummary, cancellationToken);
    }

    public async Task CreateBusinessGroupChangeAsync(BusinessGroupRecord group, string changeType, string modifierLoginName, decimal previousBalance, decimal currentBalance, CancellationToken cancellationToken = default)
    {
        await CreateAsync(group.Id, changeType, group.Name, string.Empty, modifierLoginName, previousBalance, currentBalance, string.Empty, cancellationToken);
    }

    private async Task CreateAsync(
        long recordId,
        string orderNo,
        string businessGroupName,
        string receiverName,
        string modifierLoginName,
        decimal previousAmount,
        decimal currentAmount,
        string changeSummary,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO order_change_logs
                (order_upload_id, order_no, business_group_name, receiver_name, modifier_login_name, changed_at_utc, previous_amount, current_amount, amount_difference, change_summary)
            VALUES
                (@orderUploadId, @orderNo, @businessGroupName, @receiverName, @modifierLoginName, @changedAtUtc, @previousAmount, @currentAmount, @amountDifference, @changeSummary);
            """;
        command.Parameters.AddWithValue("@orderUploadId", recordId);
        command.Parameters.AddWithValue("@orderNo", orderNo.Trim());
        command.Parameters.AddWithValue("@businessGroupName", businessGroupName.Trim());
        command.Parameters.AddWithValue("@receiverName", receiverName.Trim());
        command.Parameters.AddWithValue("@modifierLoginName", modifierLoginName.Trim());
        command.Parameters.AddWithValue("@changedAtUtc", FormatDate(DateTime.UtcNow));
        command.Parameters.AddWithValue("@previousAmount", previousAmount);
        command.Parameters.AddWithValue("@currentAmount", currentAmount);
        command.Parameters.AddWithValue("@amountDifference", currentAmount - previousAmount);
        command.Parameters.AddWithValue("@changeSummary", changeSummary.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PagedQueryResult<OrderChangeLogRecord>> QueryAsync(OrderChangeLogQuery query, CancellationToken cancellationToken = default)
    {
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);
        var (whereSql, parameters) = BuildWhereClause(query);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(1) FROM order_change_logs{whereSql};";
        AddParameters(countCommand, parameters);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, order_no, business_group_name, receiver_name, modifier_login_name, changed_at_utc,
                   previous_amount, current_amount, amount_difference, change_summary
            FROM order_change_logs
            {whereSql}
            ORDER BY changed_at_utc DESC, id DESC
            LIMIT @limit OFFSET @offset;
            """;
        AddParameters(command, parameters);
        command.Parameters.AddWithValue("@limit", pageSize);
        command.Parameters.AddWithValue("@offset", (pageNumber - 1) * pageSize);
        var records = new List<OrderChangeLogRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new OrderChangeLogRecord
            {
                Id = reader.GetInt64("id"),
                OrderNo = reader.GetString("order_no"),
                BusinessGroupName = reader.GetString("business_group_name"),
                ReceiverName = reader.GetString("receiver_name"),
                ModifierLoginName = reader.GetString("modifier_login_name"),
                ChangedAtUtc = DbValueReader.ReadUtcDateTime(reader, "changed_at_utc"),
                PreviousAmount = reader.GetDecimal("previous_amount"),
                CurrentAmount = reader.GetDecimal("current_amount"),
                AmountDifference = reader.GetDecimal("amount_difference"),
                ChangeSummary = DbValueReader.ReadString(reader, "change_summary")
            });
        }

        return new PagedQueryResult<OrderChangeLogRecord> { TotalCount = totalCount, PageNumber = pageNumber, PageSize = pageSize, Items = records };
    }

    private static async Task EnsureTableAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS order_change_logs (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                order_upload_id BIGINT NOT NULL,
                order_no VARCHAR(128) NOT NULL,
                business_group_name VARCHAR(128) NOT NULL,
                receiver_name VARCHAR(128) NOT NULL,
                modifier_login_name VARCHAR(64) NOT NULL,
                changed_at_utc DATETIME(6) NOT NULL,
                previous_amount DECIMAL(18,2) NOT NULL,
                current_amount DECIMAL(18,2) NOT NULL,
                amount_difference DECIMAL(18,2) NOT NULL,
                change_summary TEXT NULL,
                KEY idx_order_change_logs_changed_at (changed_at_utc DESC, id DESC),
                KEY idx_order_change_logs_order_no (order_no),
                KEY idx_order_change_logs_group_name (business_group_name)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureChangeSummaryColumnAsync(connection, cancellationToken);
    }

    private static async Task EnsureChangeSummaryColumnAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = """
            SELECT COUNT(1)
            FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'order_change_logs'
              AND column_name = 'change_summary';
            """;
        var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken)) > 0;
        if (exists)
        {
            return;
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE order_change_logs ADD COLUMN change_summary TEXT NULL AFTER amount_difference;";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static (string WhereSql, Dictionary<string, object> Parameters) BuildWhereClause(OrderChangeLogQuery query)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();
        AddLikeFilter(clauses, parameters, "receiver_name", "@receiverName", query.ReceiverName);
        AddLikeFilter(clauses, parameters, "modifier_login_name", "@modifierLoginName", query.ModifierLoginName);
        AddLikeFilter(clauses, parameters, "business_group_name", "@businessGroupName", query.BusinessGroupName);
        AddLikeFilter(clauses, parameters, "order_no", "@orderNo", query.OrderNo);
        if (query.ChangedAtStartUtc.HasValue) { clauses.Add("changed_at_utc >= @changedAtStartUtc"); parameters["@changedAtStartUtc"] = FormatDate(query.ChangedAtStartUtc.Value); }
        if (query.ChangedAtEndUtc.HasValue) { clauses.Add("changed_at_utc <= @changedAtEndUtc"); parameters["@changedAtEndUtc"] = FormatDate(query.ChangedAtEndUtc.Value); }
        return clauses.Count == 0 ? (string.Empty, parameters) : ($" WHERE {string.Join(" AND ", clauses)}", parameters);
    }

    private static void AddLikeFilter(List<string> clauses, Dictionary<string, object> parameters, string columnName, string parameterName, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) { clauses.Add($"{columnName} LIKE {parameterName}"); parameters[parameterName] = $"%{value.Trim()}%"; }
    }

    private static void AddParameters(MySqlCommand command, Dictionary<string, object> parameters)
    {
        foreach (var parameter in parameters) { command.Parameters.AddWithValue(parameter.Key, parameter.Value); }
    }

    private static string FormatDate(DateTime value) => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
