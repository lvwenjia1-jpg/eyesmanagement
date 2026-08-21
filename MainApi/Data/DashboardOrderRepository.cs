using System.Globalization;
using MainApi.Domain;
using MySqlConnector;

namespace MainApi.Data;

public sealed class DashboardOrderRepository
{
    private const string CancelledStatus = "已取消";
    private const string SortByOrderNo = "orderNo";
    private const string SortByUploaderLoginName = "uploaderLoginName";
    private const string SortByReceiverName = "receiverName";
    private const string SortByAmount = "amount";
    private const string SortByTrackingNumber = "trackingNumber";
    private const string SortByStatus = "status";
    private const string SortByCreatedAtUtc = "createdAtUtc";
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
                u.receiver_mobile,
                u.receiver_address,
                u.amount,
                u.tracking_number,
                u.status,
                0 AS has_special_price,
                '' AS special_price_summary,
                u.created_at_utc
            FROM order_uploads u
            {whereSql}
            ORDER BY {BuildOrderByClause(normalized.SortBy, normalized.SortDirection)}
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
                    ReceiverMobile = DbValueReader.ReadString(reader, "receiver_mobile"),
                    ReceiverAddress = reader.GetString(reader.GetOrdinal("receiver_address")),
                    Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
                    TrackingNumber = DbValueReader.ReadString(reader, "tracking_number"),
                    Status = reader.GetString(reader.GetOrdinal("status")),
                    IsCancelled = string.Equals(reader.GetString(reader.GetOrdinal("status")), "已取消", StringComparison.OrdinalIgnoreCase),
                    HasSpecialPrice = reader.GetInt64(reader.GetOrdinal("has_special_price")) == 1,
                    SpecialPriceSummary = reader.GetString(reader.GetOrdinal("special_price_summary")),
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

    public async Task<IReadOnlyList<DashboardOrderSummaryRecord>> ListByBusinessGroupForExportAsync(
        DashboardOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeQuery(query);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var (whereSql, parameters) = BuildWhereClause(normalized);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                u.id,
                COALESCE(NULLIF(u.order_number, ''), u.upload_no) AS order_no,
                u.uploader_login_name,
                u.receiver_name,
                u.receiver_mobile,
                u.receiver_address,
                u.amount,
                u.tracking_number,
                u.status,
                u.created_at_utc
            FROM order_uploads u
            {whereSql}
            ORDER BY {BuildOrderByClause(normalized.SortBy, normalized.SortDirection)};
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }

        var items = new List<DashboardOrderSummaryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapSummaryRecord(reader));
        }

        return items;
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
                u.receiver_mobile,
                u.receiver_address,
                u.amount,
                u.tracking_number,
                u.status,
                0 AS has_special_price,
                '' AS special_price_summary,
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
                ReceiverMobile = DbValueReader.ReadString(reader, "receiver_mobile"),
                ReceiverAddress = reader.GetString(reader.GetOrdinal("receiver_address")),
                Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
                TrackingNumber = DbValueReader.ReadString(reader, "tracking_number"),
                Status = reader.GetString(reader.GetOrdinal("status")),
                IsCancelled = string.Equals(reader.GetString(reader.GetOrdinal("status")), "已取消", StringComparison.OrdinalIgnoreCase),
                HasSpecialPrice = reader.GetInt64(reader.GetOrdinal("has_special_price")) == 1,
                SpecialPriceSummary = reader.GetString(reader.GetOrdinal("special_price_summary")),
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
            SET amount = @amount,
                tracking_number = @trackingNumber,
                updated_at_utc = @updatedAtUtc
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@amount", amount);
        command.Parameters.AddWithValue("@trackingNumber", trackingNumber.Trim());
        command.Parameters.AddWithValue("@updatedAtUtc", FormatDate(DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateOrderFieldsAsync(
        long id,
        decimal amount,
        string receiverName,
        string receiverAddress,
        string receiverMobile,
        string trackingNumber,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE order_uploads
            SET amount = @amount,
                receiver_name = @receiverName,
                receiver_address = @receiverAddress,
                receiver_mobile = @receiverMobile,
                tracking_number = @trackingNumber,
                updated_at_utc = @updatedAtUtc
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@amount", amount);
        command.Parameters.AddWithValue("@receiverName", receiverName.Trim());
        command.Parameters.AddWithValue("@receiverAddress", receiverAddress.Trim());
        command.Parameters.AddWithValue("@receiverMobile", receiverMobile.Trim());
        command.Parameters.AddWithValue("@trackingNumber", trackingNumber.Trim());
        command.Parameters.AddWithValue("@updatedAtUtc", FormatDate(DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DashboardOrderTrackingSyncTarget>> ListTrackingSyncTargetsAsync(
        long businessGroupId,
        DateTime? startTimeUtc,
        DateTime? endTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var clauses = new List<string>
        {
            "TRIM(COALESCE(u.order_number, '')) <> ''",
            "TRIM(COALESCE(u.tracking_number, '')) = ''"
        };

        if (businessGroupId > 0)
        {
            clauses.Insert(0, "u.business_group_id = @businessGroupId");
            command.Parameters.AddWithValue("@businessGroupId", businessGroupId);
        }

        AddCancelledStatusParameter(command);
        AppendExcludeCancelledOrderClauses(clauses, "u");

        if (startTimeUtc.HasValue)
        {
            clauses.Add("u.created_at_utc >= @startTimeUtc");
            command.Parameters.AddWithValue("@startTimeUtc", FormatDate(startTimeUtc.Value));
        }

        if (endTimeUtc.HasValue)
        {
            clauses.Add("u.created_at_utc <= @endTimeUtc");
            command.Parameters.AddWithValue("@endTimeUtc", FormatDate(endTimeUtc.Value));
        }

        command.CommandText = $"""
            SELECT
                u.id,
                u.order_number,
                u.created_at_utc,
                u.tracking_number
            FROM order_uploads u
            WHERE {string.Join(" AND ", clauses)}
            ORDER BY u.created_at_utc DESC, u.id DESC;
            """;

        var result = new List<DashboardOrderTrackingSyncTarget>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DashboardOrderTrackingSyncTarget
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                OrderNumber = reader.GetString(reader.GetOrdinal("order_number")),
                CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc"),
                TrackingNumber = DbValueReader.ReadString(reader, "tracking_number")
            });
        }

        return result;
    }

    public async Task<bool> UpdateTrackingNumberAsync(long id, string trackingNumber, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE order_uploads
            SET tracking_number = @trackingNumber,
                updated_at_utc = @updatedAtUtc
            WHERE id = @id
              AND TRIM(COALESCE(tracking_number, '')) = ''
              AND TRIM(COALESCE(status, '')) <> @cancelledStatus;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@trackingNumber", trackingNumber.Trim());
        command.Parameters.AddWithValue("@updatedAtUtc", FormatDate(DateTime.UtcNow));
        AddCancelledStatusParameter(command);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM order_uploads WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
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
            SELECT id, order_upload_id, product_code, product_name, price_name, unit_price, line_amount, quantity
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
                PriceName = reader.GetString(reader.GetOrdinal("price_name")),
                UnitPrice = reader.GetInt32(reader.GetOrdinal("unit_price")),
                LineAmount = reader.GetInt32(reader.GetOrdinal("line_amount")),
                Quantity = reader.GetInt32(reader.GetOrdinal("quantity"))
            });
        }

        foreach (var pair in buffer)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static DashboardOrderSummaryRecord MapSummaryRecord(MySqlDataReader reader)
    {
        var status = reader.GetString(reader.GetOrdinal("status"));
        return new DashboardOrderSummaryRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            OrderNo = reader.GetString(reader.GetOrdinal("order_no")),
            UploaderLoginName = reader.GetString(reader.GetOrdinal("uploader_login_name")),
            ReceiverName = reader.GetString(reader.GetOrdinal("receiver_name")),
            ReceiverMobile = DbValueReader.ReadString(reader, "receiver_mobile"),
            ReceiverAddress = reader.GetString(reader.GetOrdinal("receiver_address")),
            Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
            TrackingNumber = DbValueReader.ReadString(reader, "tracking_number"),
            Status = status,
            IsCancelled = string.Equals(status, CancelledStatus, StringComparison.OrdinalIgnoreCase),
            HasSpecialPrice = TryReadBooleanFlag(reader, "has_special_price"),
            SpecialPriceSummary = TryReadOptionalString(reader, "special_price_summary"),
            CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc")
        };
    }

    private static bool TryReadBooleanFlag(MySqlDataReader reader, string columnName)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return !reader.IsDBNull(ordinal) && reader.GetInt64(ordinal) == 1;
        }
        catch (IndexOutOfRangeException)
        {
            return false;
        }
    }

    private static string TryReadOptionalString(MySqlDataReader reader, string columnName)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return string.Empty;
        }
    }

    private static DashboardOrderQuery NormalizeQuery(DashboardOrderQuery query)
    {
        return new DashboardOrderQuery
        {
            BusinessGroupId = query.BusinessGroupId,
            PageNumber = Math.Max(1, query.PageNumber),
            PageSize = Math.Clamp(query.PageSize, 1, 200),
            StartTimeUtc = query.StartTimeUtc?.ToUniversalTime(),
            EndTimeUtc = query.EndTimeUtc?.ToUniversalTime(),
            OrderNo = query.OrderNo?.Trim() ?? string.Empty,
            ReceiverName = query.ReceiverName?.Trim() ?? string.Empty,
            HasTrackingNumber = query.HasTrackingNumber,
            ExcludeCancelledOrders = query.ExcludeCancelledOrders,
            SortBy = NormalizeSortBy(query.SortBy),
            SortDirection = NormalizeSortDirection(query.SortDirection)
        };
    }

    private static (string WhereSql, Dictionary<string, object> Parameters) BuildWhereClause(DashboardOrderQuery query)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (query.BusinessGroupId > 0)
        {
            clauses.Add("u.business_group_id = @businessGroupId");
            parameters["@businessGroupId"] = query.BusinessGroupId;
        }

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

        if (!string.IsNullOrWhiteSpace(query.OrderNo))
        {
            clauses.Add("COALESCE(NULLIF(u.order_number, ''), u.upload_no) = @orderNo");
            parameters["@orderNo"] = query.OrderNo;
        }

        if (!string.IsNullOrWhiteSpace(query.ReceiverName))
        {
            clauses.Add("u.receiver_name LIKE @receiverName");
            parameters["@receiverName"] = $"%{query.ReceiverName}%";
        }

        if (query.HasTrackingNumber.HasValue)
        {
            clauses.Add(query.HasTrackingNumber.Value
                ? "TRIM(COALESCE(u.tracking_number, '')) <> ''"
                : "TRIM(COALESCE(u.tracking_number, '')) = ''");
        }

        if (query.ExcludeCancelledOrders)
        {
            AddCancelledStatusParameter(parameters);
            AppendExcludeCancelledOrderClauses(clauses, "u");
        }

        if (clauses.Count == 0)
        {
            return (string.Empty, parameters);
        }

        return ($" WHERE {string.Join(" AND ", clauses)}", parameters);
    }

    private static void AppendExcludeCancelledOrderClauses(List<string> clauses, string orderAlias)
    {
        clauses.Add($"{orderAlias}.status <> @cancelledStatus");
        clauses.Add($"""
            (
                {orderAlias}.order_number = ''
                OR NOT EXISTS (
                    SELECT 1
                    FROM order_uploads cancelled
                    WHERE cancelled.business_group_id = {orderAlias}.business_group_id
                      AND cancelled.status = @cancelledStatus
                      AND cancelled.order_number <> ''
                      AND cancelled.order_number = {orderAlias}.order_number
                )
            )
            """);
    }

    private static void AddCancelledStatusParameter(Dictionary<string, object> parameters)
    {
        if (!parameters.ContainsKey("@cancelledStatus"))
        {
            parameters["@cancelledStatus"] = CancelledStatus;
        }
    }

    private static void AddCancelledStatusParameter(MySqlCommand command)
    {
        if (!command.Parameters.Contains("@cancelledStatus"))
        {
            command.Parameters.AddWithValue("@cancelledStatus", CancelledStatus);
        }
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string NormalizeSortBy(string? sortBy)
    {
        return sortBy?.Trim() switch
        {
            SortByOrderNo => SortByOrderNo,
            SortByUploaderLoginName => SortByUploaderLoginName,
            SortByReceiverName => SortByReceiverName,
            SortByAmount => SortByAmount,
            SortByTrackingNumber => SortByTrackingNumber,
            SortByStatus => SortByStatus,
            _ => SortByCreatedAtUtc
        };
    }

    private static string NormalizeSortDirection(string? sortDirection)
    {
        return string.Equals(sortDirection?.Trim(), "asc", StringComparison.OrdinalIgnoreCase)
            ? "ASC"
            : "DESC";
    }

    private static string BuildOrderByClause(string sortBy, string sortDirection)
    {
        return sortBy switch
        {
            SortByOrderNo => $"order_no {sortDirection}, u.id DESC",
            SortByUploaderLoginName => $"u.uploader_login_name {sortDirection}, u.created_at_utc DESC, u.id DESC",
            SortByReceiverName => $"u.receiver_name {sortDirection}, u.created_at_utc DESC, u.id DESC",
            SortByAmount => $"u.amount {sortDirection}, u.created_at_utc DESC, u.id DESC",
            SortByTrackingNumber => $"u.tracking_number {sortDirection}, u.created_at_utc DESC, u.id DESC",
            SortByStatus => $"u.status {sortDirection}, u.created_at_utc DESC, u.id DESC",
            _ => $"u.created_at_utc {sortDirection}, u.id DESC"
        };
    }
}
