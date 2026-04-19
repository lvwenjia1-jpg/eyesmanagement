using System.Globalization;
using MainApi.Domain;
using MySqlConnector;

namespace MainApi.Data;

public sealed class DashboardOrderRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public DashboardOrderRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedQueryResult<DashboardOrderSummaryRecord>> QueryByBusinessGroupAsync(DashboardOrderQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeQuery(query);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var (whereSql, parameters) = BuildWhereClause(normalized);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(1) FROM order_uploads u{whereSql};";
        foreach (var parameter in parameters)
        {
            countCommand.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                u.id,
                COALESCE(NULLIF(u.order_number, ''), u.upload_no) AS order_no,
                u.uploader_login_name,
                u.receiver_name,
                u.receiver_address,
                u.amount,
                u.tracking_number,
                u.created_at_utc
            FROM order_uploads u
            {whereSql}
            ORDER BY u.created_at_utc DESC, u.id DESC
            LIMIT @limit OFFSET @offset;
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }
        command.Parameters.AddWithValue("@limit", normalized.PageSize);
        command.Parameters.AddWithValue("@offset", (normalized.PageNumber - 1) * normalized.PageSize);

        var items = new List<DashboardOrderSummaryRecord>();
        var orderIds = new List<long>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var item = new DashboardOrderSummaryRecord
                {
                    Id = reader.GetInt64(reader.GetOrdinal("id")),
                    OrderNo = reader.GetString(reader.GetOrdinal("order_no")),
                    UploaderLoginName = reader.GetString(reader.GetOrdinal("uploader_login_name")),
                    ReceiverName = reader.GetString(reader.GetOrdinal("receiver_name")),
                    ReceiverAddress = reader.GetString(reader.GetOrdinal("receiver_address")),
                    Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
                    TrackingNumber = reader.GetString(reader.GetOrdinal("tracking_number")),
                    CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc")
                };
                items.Add(item);
                orderIds.Add(item.Id);
            }
        }

        var itemMap = await ListOrderItemsAsync(connection, orderIds, cancellationToken);
        foreach (var item in items)
        {
            item.Items = itemMap.TryGetValue(item.Id, out var orderItems) ? orderItems : Array.Empty<DashboardOrderItemRecord>();
        }

        return new PagedQueryResult<DashboardOrderSummaryRecord>
        {
            TotalCount = totalCount,
            PageNumber = normalized.PageNumber,
            PageSize = normalized.PageSize,
            Items = items
        };
    }

    public async Task<DashboardOrderDetailRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                u.id,
                COALESCE(NULLIF(u.order_number, ''), u.upload_no) AS order_no,
                u.business_group_id,
                COALESCE(bg.name, u.business_group_name, '') AS business_group_name,
                u.uploader_login_name,
                u.receiver_name,
                u.receiver_address,
                u.amount,
                u.tracking_number,
                u.created_at_utc,
                u.updated_at_utc
            FROM order_uploads u
            LEFT JOIN business_groups bg ON bg.id = u.business_group_id
            WHERE u.id = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", id);

        DashboardOrderDetailRecord? detail;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            detail = new DashboardOrderDetailRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                OrderNo = reader.GetString(reader.GetOrdinal("order_no")),
                BusinessGroupId = reader.IsDBNull(reader.GetOrdinal("business_group_id")) ? 0 : reader.GetInt64(reader.GetOrdinal("business_group_id")),
                BusinessGroupName = reader.GetString(reader.GetOrdinal("business_group_name")),
                UploaderLoginName = reader.GetString(reader.GetOrdinal("uploader_login_name")),
                ReceiverName = reader.GetString(reader.GetOrdinal("receiver_name")),
                ReceiverAddress = reader.GetString(reader.GetOrdinal("receiver_address")),
                Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
                TrackingNumber = reader.GetString(reader.GetOrdinal("tracking_number")),
                CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc"),
                UpdatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "updated_at_utc")
            };
        }

        var itemMap = await ListOrderItemsAsync(connection, new[] { id }, cancellationToken);
        detail.Items = itemMap.TryGetValue(id, out var items) ? items : Array.Empty<DashboardOrderItemRecord>();
        return detail;
    }

    public async Task UpdateAsync(long id, decimal amount, string trackingNumber, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE order_uploads
            SET tracking_number = @trackingNumber,
                updated_at_utc = @updatedAtUtc
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@trackingNumber", trackingNumber.Trim());
        command.Parameters.AddWithValue("@updatedAtUtc", FormatDate(DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<long, IReadOnlyList<DashboardOrderItemRecord>>> ListOrderItemsAsync(MySqlConnection connection, IReadOnlyCollection<long> orderIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, IReadOnlyList<DashboardOrderItemRecord>>();
        if (orderIds.Count == 0)
        {
            return result;
        }

        var ids = orderIds.Distinct().ToArray();
        var parameterNames = new List<string>();
        await using var command = connection.CreateCommand();
        for (var index = 0; index < ids.Length; index++)
        {
            var parameterName = $"@id{index}";
            parameterNames.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, ids[index]);
        }

        command.CommandText = $"""
            SELECT id, order_upload_id, product_code, product_name, quantity
            FROM order_upload_items
            WHERE order_upload_id IN ({string.Join(", ", parameterNames)})
            ORDER BY order_upload_id ASC, id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var buffer = new Dictionary<long, List<DashboardOrderItemRecord>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var orderId = reader.GetInt64(reader.GetOrdinal("order_upload_id"));
            if (!buffer.TryGetValue(orderId, out var items))
            {
                items = new List<DashboardOrderItemRecord>();
                buffer[orderId] = items;
            }

            items.Add(new DashboardOrderItemRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                ProductCode = reader.GetString(reader.GetOrdinal("product_code")),
                ProductName = reader.GetString(reader.GetOrdinal("product_name")),
                Quantity = reader.GetInt32(reader.GetOrdinal("quantity"))
            });
        }

        foreach (var pair in buffer)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static DashboardOrderQuery NormalizeQuery(DashboardOrderQuery query)
    {
        return new DashboardOrderQuery
        {
            BusinessGroupId = query.BusinessGroupId,
            PageNumber = Math.Max(1, query.PageNumber),
            PageSize = Math.Clamp(query.PageSize, 1, 200),
            StartTimeUtc = query.StartTimeUtc?.ToUniversalTime(),
            EndTimeUtc = query.EndTimeUtc?.ToUniversalTime()
        };
    }

    private static (string WhereSql, Dictionary<string, object> Parameters) BuildWhereClause(DashboardOrderQuery query)
    {
        var clauses = new List<string> { "u.business_group_id = @businessGroupId" };
        var parameters = new Dictionary<string, object> { ["@businessGroupId"] = query.BusinessGroupId };

        if (query.StartTimeUtc.HasValue)
        {
            clauses.Add("u.created_at_utc >= @startTimeUtc");
            parameters["@startTimeUtc"] = FormatDate(query.StartTimeUtc.Value);
        }

        if (query.EndTimeUtc.HasValue)
        {
            clauses.Add("u.created_at_utc <= @endTimeUtc");
            parameters["@endTimeUtc"] = FormatDate(query.EndTimeUtc.Value);
        }

        return ($" WHERE {string.Join(" AND ", clauses)}", parameters);
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
