using System.Globalization;
using MainApi.Domain;
using Microsoft.Data.Sqlite;

namespace MainApi.Data;

public sealed class UploadRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public UploadRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> CreateAsync(UploadCreateCommand commandModel, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var createdAtUtc = DateTime.UtcNow;
        var createdOn = ToDateKey(createdAtUtc);
        var createdAtText = FormatDate(createdAtUtc);
        var uploadNo = $"UP{createdOn}{createdAtUtc:HHmmssfff}{Random.Shared.Next(100, 999)}";
        long uploadId;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO order_uploads (
                    upload_no,
                    draft_id,
                    order_number,
                    session_id,
                    uploader_login_name,
                    uploader_display_name,
                    uploader_erp_id,
                    uploader_wecom_id,
                    machine_code,
                    receiver_name,
                    receiver_mobile,
                    receiver_address,
                    remark,
                    has_gift,
                    status,
                    status_detail,
                    external_request_json,
                    external_response_json,
                    item_count,
                    created_on,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES (
                    $uploadNo,
                    $draftId,
                    $orderNumber,
                    $sessionId,
                    $uploaderLoginName,
                    $uploaderDisplayName,
                    $uploaderErpId,
                    $uploaderWecomId,
                    $machineCode,
                    $receiverName,
                    $receiverMobile,
                    $receiverAddress,
                    $remark,
                    $hasGift,
                    $status,
                    $statusDetail,
                    $externalRequestJson,
                    $externalResponseJson,
                    $itemCount,
                    $createdOn,
                    $createdAtUtc,
                    $updatedAtUtc
                );
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$uploadNo", uploadNo);
            command.Parameters.AddWithValue("$draftId", commandModel.DraftId.Trim());
            command.Parameters.AddWithValue("$orderNumber", commandModel.OrderNumber.Trim());
            command.Parameters.AddWithValue("$sessionId", commandModel.SessionId.Trim());
            command.Parameters.AddWithValue("$uploaderLoginName", commandModel.UploaderLoginName.Trim());
            command.Parameters.AddWithValue("$uploaderDisplayName", commandModel.UploaderDisplayName.Trim());
            command.Parameters.AddWithValue("$uploaderErpId", commandModel.UploaderErpId.Trim());
            command.Parameters.AddWithValue("$uploaderWecomId", commandModel.UploaderWecomId.Trim());
            command.Parameters.AddWithValue("$machineCode", commandModel.MachineCode.Trim());
            command.Parameters.AddWithValue("$receiverName", commandModel.ReceiverName.Trim());
            command.Parameters.AddWithValue("$receiverMobile", commandModel.ReceiverMobile.Trim());
            command.Parameters.AddWithValue("$receiverAddress", commandModel.ReceiverAddress.Trim());
            command.Parameters.AddWithValue("$remark", commandModel.Remark.Trim());
            command.Parameters.AddWithValue("$hasGift", commandModel.HasGift ? 1 : 0);
            command.Parameters.AddWithValue("$status", commandModel.Status.Trim());
            command.Parameters.AddWithValue("$statusDetail", commandModel.StatusDetail.Trim());
            command.Parameters.AddWithValue("$externalRequestJson", commandModel.ExternalRequestJson.Trim());
            command.Parameters.AddWithValue("$externalResponseJson", commandModel.ExternalResponseJson.Trim());
            command.Parameters.AddWithValue("$itemCount", commandModel.Items.Count);
            command.Parameters.AddWithValue("$createdOn", createdOn);
            command.Parameters.AddWithValue("$createdAtUtc", createdAtText);
            command.Parameters.AddWithValue("$updatedAtUtc", createdAtText);
            uploadId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        foreach (var item in commandModel.Items)
        {
            await using var itemCommand = connection.CreateCommand();
            itemCommand.Transaction = transaction;
            itemCommand.CommandText = """
                INSERT INTO order_upload_items (
                    order_upload_id,
                    source_text,
                    product_code,
                    product_name,
                    quantity,
                    degree_text,
                    wear_period,
                    remark,
                    is_trial
                )
                VALUES (
                    $orderUploadId,
                    $sourceText,
                    $productCode,
                    $productName,
                    $quantity,
                    $degreeText,
                    $wearPeriod,
                    $remark,
                    $isTrial
                );
                """;
            itemCommand.Parameters.AddWithValue("$orderUploadId", uploadId);
            itemCommand.Parameters.AddWithValue("$sourceText", item.SourceText.Trim());
            itemCommand.Parameters.AddWithValue("$productCode", item.ProductCode.Trim());
            itemCommand.Parameters.AddWithValue("$productName", item.ProductName.Trim());
            itemCommand.Parameters.AddWithValue("$quantity", item.Quantity);
            itemCommand.Parameters.AddWithValue("$degreeText", item.DegreeText.Trim());
            itemCommand.Parameters.AddWithValue("$wearPeriod", item.WearPeriod.Trim());
            itemCommand.Parameters.AddWithValue("$remark", item.Remark.Trim());
            itemCommand.Parameters.AddWithValue("$isTrial", item.IsTrial ? 1 : 0);
            await itemCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return uploadId;
    }

    public async Task<UploadListResult> ListAsync(UploadListQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var normalizedQuery = NormalizeQuery(query);
        var (whereSql, parameters) = BuildWhereClause(normalizedQuery);

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
                u.upload_no,
                u.draft_id,
                u.order_number,
                u.session_id,
                u.uploader_login_name,
                u.uploader_display_name,
                u.uploader_erp_id,
                u.uploader_wecom_id,
                u.machine_code,
                u.receiver_name,
                u.receiver_mobile,
                u.receiver_address,
                u.has_gift,
                u.status,
                u.status_detail,
                u.item_count,
                u.created_on,
                u.created_at_utc,
            FROM order_uploads u
            {whereSql}
            ORDER BY u.created_on DESC, u.id DESC
            LIMIT $limit OFFSET $offset;
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }
        command.Parameters.AddWithValue("$limit", normalizedQuery.PageSize);
        command.Parameters.AddWithValue("$offset", (normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var uploads = new List<UploadSummaryRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            uploads.Add(new UploadSummaryRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                UploadNo = reader.GetString(reader.GetOrdinal("upload_no")),
                DraftId = reader.GetString(reader.GetOrdinal("draft_id")),
                OrderNumber = reader.GetString(reader.GetOrdinal("order_number")),
                SessionId = reader.GetString(reader.GetOrdinal("session_id")),
                UploaderLoginName = reader.GetString(reader.GetOrdinal("uploader_login_name")),
                UploaderDisplayName = reader.GetString(reader.GetOrdinal("uploader_display_name")),
                UploaderErpId = reader.GetString(reader.GetOrdinal("uploader_erp_id")),
                UploaderWecomId = reader.GetString(reader.GetOrdinal("uploader_wecom_id")),
                MachineCode = reader.GetString(reader.GetOrdinal("machine_code")),
                ReceiverName = reader.GetString(reader.GetOrdinal("receiver_name")),
                ReceiverMobile = reader.GetString(reader.GetOrdinal("receiver_mobile")),
                ReceiverAddress = reader.GetString(reader.GetOrdinal("receiver_address")),
                HasGift = reader.GetInt64(reader.GetOrdinal("has_gift")) == 1,
                Status = reader.GetString(reader.GetOrdinal("status")),
                StatusDetail = reader.GetString(reader.GetOrdinal("status_detail")),
                ItemCount = reader.GetInt32(reader.GetOrdinal("item_count")),
                CreatedOn = reader.GetInt32(reader.GetOrdinal("created_on")),
                CreatedAtUtc = ParseDate(reader.GetString(reader.GetOrdinal("created_at_utc")))
            });
        }

        return new UploadListResult
        {
            TotalCount = totalCount,
            PageNumber = normalizedQuery.PageNumber,
            PageSize = normalizedQuery.PageSize,
            Items = uploads
        };
    }

    public async Task<UploadDetailRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                upload_no,
                draft_id,
                order_number,
                session_id,
                uploader_login_name,
                uploader_display_name,
                uploader_erp_id,
                uploader_wecom_id,
                machine_code,
                receiver_name,
                receiver_mobile,
                receiver_address,
                remark,
                has_gift,
                status,
                status_detail,
                external_request_json,
                external_response_json,
                created_at_utc,
                updated_at_utc
            FROM order_uploads
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var detail = new UploadDetailRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            UploadNo = reader.GetString(reader.GetOrdinal("upload_no")),
            DraftId = reader.GetString(reader.GetOrdinal("draft_id")),
            OrderNumber = reader.GetString(reader.GetOrdinal("order_number")),
            SessionId = reader.GetString(reader.GetOrdinal("session_id")),
            UploaderLoginName = reader.GetString(reader.GetOrdinal("uploader_login_name")),
            UploaderDisplayName = reader.GetString(reader.GetOrdinal("uploader_display_name")),
            UploaderErpId = reader.GetString(reader.GetOrdinal("uploader_erp_id")),
            UploaderWecomId = reader.GetString(reader.GetOrdinal("uploader_wecom_id")),
            MachineCode = reader.GetString(reader.GetOrdinal("machine_code")),
            ReceiverName = reader.GetString(reader.GetOrdinal("receiver_name")),
            ReceiverMobile = reader.GetString(reader.GetOrdinal("receiver_mobile")),
            ReceiverAddress = reader.GetString(reader.GetOrdinal("receiver_address")),
            Remark = reader.GetString(reader.GetOrdinal("remark")),
            HasGift = reader.GetInt64(reader.GetOrdinal("has_gift")) == 1,
            Status = reader.GetString(reader.GetOrdinal("status")),
            StatusDetail = reader.GetString(reader.GetOrdinal("status_detail")),
            ExternalRequestJson = reader.GetString(reader.GetOrdinal("external_request_json")),
            ExternalResponseJson = reader.GetString(reader.GetOrdinal("external_response_json")),
            CreatedAtUtc = ParseDate(reader.GetString(reader.GetOrdinal("created_at_utc"))),
            UpdatedAtUtc = ParseDate(reader.GetString(reader.GetOrdinal("updated_at_utc")))
        };

        detail.Items = await ListItemsAsync(connection, id, cancellationToken);
        detail.ItemCount = detail.Items.Count;
        return detail;
    }

    private static async Task<IReadOnlyList<UploadItemRecord>> ListItemsAsync(SqliteConnection connection, long uploadId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_text, product_code, product_name, quantity, degree_text, wear_period, remark, is_trial
            FROM order_upload_items
            WHERE order_upload_id = $uploadId
            ORDER BY id ASC;
            """;
        command.Parameters.AddWithValue("$uploadId", uploadId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<UploadItemRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new UploadItemRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                SourceText = reader.GetString(reader.GetOrdinal("source_text")),
                ProductCode = reader.GetString(reader.GetOrdinal("product_code")),
                ProductName = reader.GetString(reader.GetOrdinal("product_name")),
                Quantity = reader.GetInt32(reader.GetOrdinal("quantity")),
                DegreeText = reader.GetString(reader.GetOrdinal("degree_text")),
                WearPeriod = reader.GetString(reader.GetOrdinal("wear_period")),
                Remark = reader.GetString(reader.GetOrdinal("remark")),
                IsTrial = reader.GetInt64(reader.GetOrdinal("is_trial")) == 1
            });
        }

        return items;
    }

    private static DateTime ParseDate(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static UploadListQuery NormalizeQuery(UploadListQuery query)
    {
        return new UploadListQuery
        {
            PageNumber = Math.Max(1, query.PageNumber),
            PageSize = Math.Clamp(query.PageSize, 1, 500),
            CreatedOn = query.CreatedOn,
            CreatedOnFrom = query.CreatedOnFrom,
            CreatedOnTo = query.CreatedOnTo,
            MachineCode = query.MachineCode.Trim(),
            Status = query.Status.Trim()
        };
    }

    private static (string WhereSql, Dictionary<string, object> Parameters) BuildWhereClause(UploadListQuery query)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (query.CreatedOn.HasValue)
        {
            clauses.Add("u.created_on = $createdOn");
            parameters["$createdOn"] = query.CreatedOn.Value;
        }
        else
        {
            if (query.CreatedOnFrom.HasValue)
            {
                clauses.Add("u.created_on >= $createdOnFrom");
                parameters["$createdOnFrom"] = query.CreatedOnFrom.Value;
            }

            if (query.CreatedOnTo.HasValue)
            {
                clauses.Add("u.created_on <= $createdOnTo");
                parameters["$createdOnTo"] = query.CreatedOnTo.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(query.MachineCode))
        {
            clauses.Add("u.machine_code = $machineCode");
            parameters["$machineCode"] = query.MachineCode;
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            clauses.Add("u.status = $status");
            parameters["$status"] = query.Status;
        }

        return clauses.Count == 0
            ? (string.Empty, parameters)
            : ($" WHERE {string.Join(" AND ", clauses)}", parameters);
    }

    private static int ToDateKey(DateTime value)
    {
        return int.Parse(value.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
