using System.Globalization;
using MainApi.Domain;
using MainApi.Services;
using MySqlConnector;

namespace MainApi.Data;

public sealed class UploadRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public UploadRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> CreateAsync(UploadCreateCommand commandModel, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var lockName = BuildOrderUploadLockName(commandModel.OrderNumber);
        if (!string.IsNullOrWhiteSpace(lockName))
        {
            await AcquireNamedLockAsync(connection, lockName, cancellationToken);
        }

        try
        {
            await using var transaction = connection.BeginTransaction();

            var normalizedStatus = NormalizeStatus(commandModel.Status);
            if (!string.IsNullOrWhiteSpace(commandModel.OrderNumber))
            {
                await EnsureOrderNumberWriteAllowedAsync(
                    connection,
                    transaction,
                    commandModel.OrderNumber,
                    normalizedStatus,
                    cancellationToken);
            }

            var createdAtUtc = NormalizeCreatedAtUtc(commandModel.CreatedAtUtc);
            var createdOn = ToDateKey(createdAtUtc);
            var createdAtText = FormatDate(createdAtUtc);
            var uploadNo = $"UP{createdOn}{createdAtUtc:HHmmssfff}{Random.Shared.Next(100, 999)}";

            var priceRules = await LoadActivePriceRulesAsync(connection, transaction, cancellationToken);
            var catalogEntries = await LoadCatalogEntriesByProductCodesAsync(
                connection,
                transaction,
                commandModel.Items.Select(item => item.ProductCode).ToArray(),
                cancellationToken);

            var pricingInputs = commandModel.Items.Select(item =>
            {
                catalogEntries.TryGetValue(item.ProductCode.Trim(), out var catalogEntry);
                return new OrderPricingCalculator.OrderPricingInputItem
                {
                    SpecificationToken = catalogEntry?.SpecificationToken?.Trim() ?? item.WearPeriod.Trim(),
                    ModelToken = catalogEntry?.ModelToken?.Trim() ?? string.Empty,
                    Quantity = item.Quantity
                };
            }).ToArray();
            var pricingResults = OrderPricingCalculator.Calculate(pricingInputs, priceRules)
                .ToDictionary(result => result.ItemIndex);

            var itemPricingRows = new List<ItemPricingRow>(commandModel.Items.Count);
            for (var itemIndex = 0; itemIndex < commandModel.Items.Count; itemIndex++)
            {
                var item = commandModel.Items[itemIndex];
                var pricing = pricingResults.GetValueOrDefault(itemIndex) ?? new OrderPricingCalculator.OrderPricingLineResult();
                itemPricingRows.Add(new ItemPricingRow(item, pricing.PriceRuleId, pricing.PriceName, pricing.UnitPrice, pricing.LineAmount));
            }

            var totalAmount = itemPricingRows.Sum(row => row.LineAmount);
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
                        business_group_id,
                        business_group_name,
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
                        raw_text,
                        snapshot_json,
                        amount,
                        tracking_number,
                        external_request_json,
                        external_response_json,
                        item_count,
                        created_on,
                        created_at_utc,
                        updated_at_utc
                    )
                    VALUES (
                        @uploadNo,
                        @draftId,
                        @orderNumber,
                        @sessionId,
                        @businessGroupId,
                        @businessGroupName,
                        @uploaderLoginName,
                        @uploaderDisplayName,
                        @uploaderErpId,
                        @uploaderWecomId,
                        @machineCode,
                        @receiverName,
                        @receiverMobile,
                        @receiverAddress,
                        @remark,
                        @hasGift,
                        @status,
                        @statusDetail,
                        @rawText,
                        @snapshotJson,
                        @amount,
                        @trackingNumber,
                        @externalRequestJson,
                        @externalResponseJson,
                        @itemCount,
                        @createdOn,
                        @createdAtUtc,
                        @updatedAtUtc
                    );
                    """;
                command.Parameters.AddWithValue("@uploadNo", uploadNo);
                command.Parameters.AddWithValue("@draftId", commandModel.DraftId.Trim());
                command.Parameters.AddWithValue("@orderNumber", commandModel.OrderNumber.Trim());
                command.Parameters.AddWithValue("@sessionId", commandModel.SessionId.Trim());
                command.Parameters.AddWithValue("@businessGroupId", (object?)commandModel.BusinessGroupId ?? DBNull.Value);
                command.Parameters.AddWithValue("@businessGroupName", commandModel.BusinessGroupName.Trim());
                command.Parameters.AddWithValue("@uploaderLoginName", commandModel.UploaderLoginName.Trim());
                command.Parameters.AddWithValue("@uploaderDisplayName", commandModel.UploaderDisplayName.Trim());
                command.Parameters.AddWithValue("@uploaderErpId", commandModel.UploaderErpId.Trim());
                command.Parameters.AddWithValue("@uploaderWecomId", commandModel.UploaderWecomId.Trim());
                command.Parameters.AddWithValue("@machineCode", commandModel.MachineCode.Trim());
                command.Parameters.AddWithValue("@receiverName", commandModel.ReceiverName.Trim());
                command.Parameters.AddWithValue("@receiverMobile", commandModel.ReceiverMobile.Trim());
                command.Parameters.AddWithValue("@receiverAddress", commandModel.ReceiverAddress.Trim());
                command.Parameters.AddWithValue("@remark", commandModel.Remark.Trim());
                command.Parameters.AddWithValue("@hasGift", commandModel.HasGift ? 1 : 0);
                command.Parameters.AddWithValue("@status", normalizedStatus);
                command.Parameters.AddWithValue("@statusDetail", commandModel.StatusDetail.Trim());
                command.Parameters.AddWithValue("@rawText", commandModel.RawText.Trim());
                command.Parameters.AddWithValue("@snapshotJson", commandModel.SnapshotJson.Trim());
                command.Parameters.AddWithValue("@amount", totalAmount);
                command.Parameters.AddWithValue("@trackingNumber", commandModel.TrackingNumber.Trim());
                command.Parameters.AddWithValue("@externalRequestJson", commandModel.ExternalRequestJson.Trim());
                command.Parameters.AddWithValue("@externalResponseJson", commandModel.ExternalResponseJson.Trim());
                command.Parameters.AddWithValue("@itemCount", commandModel.Items.Count);
                command.Parameters.AddWithValue("@createdOn", createdOn);
                command.Parameters.AddWithValue("@createdAtUtc", createdAtText);
                command.Parameters.AddWithValue("@updatedAtUtc", createdAtText);
                await command.ExecuteNonQueryAsync(cancellationToken);
                uploadId = command.LastInsertedId;
            }

            foreach (var row in itemPricingRows)
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
                        is_trial,
                        price_rule_id,
                        price_name,
                        unit_price,
                        line_amount
                    )
                    VALUES (
                        @orderUploadId,
                        @sourceText,
                        @productCode,
                        @productName,
                        @quantity,
                        @degreeText,
                        @wearPeriod,
                        @remark,
                        @isTrial,
                        @priceRuleId,
                        @priceName,
                        @unitPrice,
                        @lineAmount
                    );
                    """;
                itemCommand.Parameters.AddWithValue("@orderUploadId", uploadId);
                itemCommand.Parameters.AddWithValue("@sourceText", row.Item.SourceText.Trim());
                itemCommand.Parameters.AddWithValue("@productCode", row.Item.ProductCode.Trim());
                itemCommand.Parameters.AddWithValue("@productName", row.Item.ProductName.Trim());
                itemCommand.Parameters.AddWithValue("@quantity", row.Item.Quantity);
                itemCommand.Parameters.AddWithValue("@degreeText", row.Item.DegreeText.Trim());
                itemCommand.Parameters.AddWithValue("@wearPeriod", row.Item.WearPeriod.Trim());
                itemCommand.Parameters.AddWithValue("@remark", row.Item.Remark.Trim());
                itemCommand.Parameters.AddWithValue("@isTrial", row.Item.IsTrial ? 1 : 0);
                itemCommand.Parameters.AddWithValue("@priceRuleId", (object?)row.PriceRuleId ?? DBNull.Value);
                itemCommand.Parameters.AddWithValue("@priceName", row.PriceName);
                itemCommand.Parameters.AddWithValue("@unitPrice", row.UnitPrice);
                itemCommand.Parameters.AddWithValue("@lineAmount", row.LineAmount);
                await itemCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return uploadId;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(lockName))
            {
                await ReleaseNamedLockAsync(connection, lockName, cancellationToken);
            }
        }
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
                u.business_group_id,
                u.business_group_name,
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
                u.raw_text,
                u.snapshot_json,
                u.external_response_json,
                u.amount,
                u.tracking_number,
                u.item_count,
                u.created_on,
                u.created_at_utc
            FROM order_uploads u
            {whereSql}
            ORDER BY u.created_on DESC, u.id DESC
            LIMIT @limit OFFSET @offset;
            """;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }
        command.Parameters.AddWithValue("@limit", normalizedQuery.PageSize);
        command.Parameters.AddWithValue("@offset", (normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize);

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
                BusinessGroupId = reader.IsDBNull(reader.GetOrdinal("business_group_id")) ? null : reader.GetInt64(reader.GetOrdinal("business_group_id")),
                BusinessGroupName = reader.GetString(reader.GetOrdinal("business_group_name")),
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
                RawText = reader.GetString(reader.GetOrdinal("raw_text")),
                SnapshotJson = reader.GetString(reader.GetOrdinal("snapshot_json")),
                ResponseText = reader.GetString(reader.GetOrdinal("external_response_json")),
                Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
                TrackingNumber = reader.GetString(reader.GetOrdinal("tracking_number")),
                ItemCount = reader.GetInt32(reader.GetOrdinal("item_count")),
                CreatedOn = reader.GetInt32(reader.GetOrdinal("created_on")),
                CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc")
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
                business_group_id,
                business_group_name,
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
                raw_text,
                snapshot_json,
                amount,
                tracking_number,
                external_request_json,
                external_response_json,
                created_at_utc,
                updated_at_utc
            FROM order_uploads
            WHERE id = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", id);

        UploadDetailRecord? detail;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            detail = new UploadDetailRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                UploadNo = reader.GetString(reader.GetOrdinal("upload_no")),
                DraftId = reader.GetString(reader.GetOrdinal("draft_id")),
                OrderNumber = reader.GetString(reader.GetOrdinal("order_number")),
                SessionId = reader.GetString(reader.GetOrdinal("session_id")),
                BusinessGroupId = reader.IsDBNull(reader.GetOrdinal("business_group_id")) ? null : reader.GetInt64(reader.GetOrdinal("business_group_id")),
                BusinessGroupName = reader.GetString(reader.GetOrdinal("business_group_name")),
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
                RawText = reader.GetString(reader.GetOrdinal("raw_text")),
                SnapshotJson = reader.GetString(reader.GetOrdinal("snapshot_json")),
                ResponseText = reader.GetString(reader.GetOrdinal("external_response_json")),
                Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
                TrackingNumber = reader.GetString(reader.GetOrdinal("tracking_number")),
                ExternalRequestJson = reader.GetString(reader.GetOrdinal("external_request_json")),
                ExternalResponseJson = reader.GetString(reader.GetOrdinal("external_response_json")),
                CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc"),
                UpdatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "updated_at_utc")
            };
        }

        detail.Items = await ListItemsAsync(connection, id, cancellationToken);
        detail.ItemCount = detail.Items.Count;
        return detail;
    }

    public async Task UpdateAsync(long id, string trackingNumber, CancellationToken cancellationToken = default)
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

    public async Task<UploadDetailRecord?> FindByBusinessOrderIdAsync(long businessOrderId, CancellationToken cancellationToken = default)
    {
        return await FindByIdAsync(businessOrderId, cancellationToken);
    }

    private static async Task<IReadOnlyList<UploadItemRecord>> ListItemsAsync(MySqlConnection connection, long uploadId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                source_text,
                product_code,
                product_name,
                quantity,
                degree_text,
                wear_period,
                remark,
                is_trial,
                price_rule_id,
                price_name,
                unit_price,
                line_amount
            FROM order_upload_items
            WHERE order_upload_id = @uploadId
            ORDER BY id ASC;
            """;
        command.Parameters.AddWithValue("@uploadId", uploadId);

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
                PriceRuleId = reader.IsDBNull(reader.GetOrdinal("price_rule_id")) ? null : reader.GetInt64(reader.GetOrdinal("price_rule_id")),
                PriceName = reader.GetString(reader.GetOrdinal("price_name")),
                UnitPrice = reader.GetInt32(reader.GetOrdinal("unit_price")),
                LineAmount = reader.GetInt32(reader.GetOrdinal("line_amount")),
                DegreeText = reader.GetString(reader.GetOrdinal("degree_text")),
                WearPeriod = reader.GetString(reader.GetOrdinal("wear_period")),
                Remark = reader.GetString(reader.GetOrdinal("remark")),
                IsTrial = reader.GetInt64(reader.GetOrdinal("is_trial")) == 1
            });
        }

        return items;
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
            Status = query.Status.Trim(),
            UploaderLoginName = query.UploaderLoginName.Trim(),
            BusinessGroupId = query.BusinessGroupId,
            OrderNumber = query.OrderNumber.Trim(),
            ReceiverKeyword = query.ReceiverKeyword.Trim(),
            DraftId = query.DraftId.Trim()
        };
    }

    private static (string WhereSql, Dictionary<string, object> Parameters) BuildWhereClause(UploadListQuery query)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (query.CreatedOn.HasValue)
        {
            clauses.Add("u.created_on = @createdOn");
            parameters["@createdOn"] = query.CreatedOn.Value;
        }
        else
        {
            if (query.CreatedOnFrom.HasValue)
            {
                clauses.Add("u.created_on >= @createdOnFrom");
                parameters["@createdOnFrom"] = query.CreatedOnFrom.Value;
            }

            if (query.CreatedOnTo.HasValue)
            {
                clauses.Add("u.created_on <= @createdOnTo");
                parameters["@createdOnTo"] = query.CreatedOnTo.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(query.MachineCode))
        {
            clauses.Add("u.machine_code = @machineCode");
            parameters["@machineCode"] = query.MachineCode;
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            clauses.Add("u.status = @status");
            parameters["@status"] = query.Status;
        }

        if (!string.IsNullOrWhiteSpace(query.UploaderLoginName))
        {
            clauses.Add("u.uploader_login_name = @uploaderLoginName");
            parameters["@uploaderLoginName"] = query.UploaderLoginName;
        }

        if (query.BusinessGroupId.HasValue)
        {
            clauses.Add("u.business_group_id = @businessGroupId");
            parameters["@businessGroupId"] = query.BusinessGroupId.Value;
        }

        if (!string.IsNullOrWhiteSpace(query.OrderNumber))
        {
            clauses.Add("u.order_number = @orderNumber");
            parameters["@orderNumber"] = query.OrderNumber;
        }

        if (!string.IsNullOrWhiteSpace(query.ReceiverKeyword))
        {
            clauses.Add("""
                (
                    u.receiver_name LIKE @receiverKeyword OR
                    u.receiver_mobile LIKE @receiverKeyword OR
                    u.receiver_address LIKE @receiverKeyword
                )
                """);
            parameters["@receiverKeyword"] = $"%{query.ReceiverKeyword}%";
        }

        if (!string.IsNullOrWhiteSpace(query.DraftId))
        {
            clauses.Add("u.draft_id LIKE @draftId");
            parameters["@draftId"] = $"%{query.DraftId}%";
        }

        return clauses.Count == 0
            ? (string.Empty, parameters)
            : ($" WHERE {string.Join(" AND ", clauses)}", parameters);
    }

    private static async Task<List<PriceRuleRecord>> LoadActivePriceRulesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = new List<PriceRuleRecord>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT id, rule_type, price_name, specification_token, model_token, required_quantity, price_value, is_active, created_at_utc, updated_at_utc
            FROM order_price_rules
            WHERE is_active = 1
            ORDER BY rule_type ASC, specification_token ASC, required_quantity DESC, model_token ASC, id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PriceRuleRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                RuleType = reader.GetString(reader.GetOrdinal("rule_type")),
                PriceName = reader.GetString(reader.GetOrdinal("price_name")),
                SpecificationToken = reader.GetString(reader.GetOrdinal("specification_token")),
                ModelToken = reader.GetString(reader.GetOrdinal("model_token")),
                RequiredQuantity = reader.GetInt32(reader.GetOrdinal("required_quantity")),
                PriceValue = reader.GetInt32(reader.GetOrdinal("price_value")),
                IsActive = reader.GetInt64(reader.GetOrdinal("is_active")) == 1,
                CreatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "created_at_utc"),
                UpdatedAtUtc = DbValueReader.ReadUtcDateTime(reader, "updated_at_utc")
            });
        }

        return result;
    }

    private static async Task<Dictionary<string, ProductCatalogEntryRecord>> LoadCatalogEntriesByProductCodesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IReadOnlyCollection<string> productCodes,
        CancellationToken cancellationToken)
    {
        var normalizedCodes = productCodes
            .Select(code => code?.Trim() ?? string.Empty)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new Dictionary<string, ProductCatalogEntryRecord>(StringComparer.OrdinalIgnoreCase);
        if (normalizedCodes.Length == 0)
        {
            return result;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var placeholders = new List<string>(normalizedCodes.Length);
        for (var index = 0; index < normalizedCodes.Length; index++)
        {
            var parameterName = $"@productCode{index}";
            placeholders.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, normalizedCodes[index]);
        }
        command.CommandText = $"""
            SELECT product_code, specification_token, model_token
            FROM product_catalog_entries
            WHERE product_code IN ({string.Join(", ", placeholders)});
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var productCode = reader.GetString(reader.GetOrdinal("product_code")).Trim();
            result[productCode] = new ProductCatalogEntryRecord
            {
                ProductCode = productCode,
                SpecificationToken = reader.GetString(reader.GetOrdinal("specification_token")),
                ModelToken = reader.GetString(reader.GetOrdinal("model_token"))
            };
        }

        return result;
    }

    private static int ToDateKey(DateTime value)
    {
        return int.Parse(value.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static DateTime NormalizeCreatedAtUtc(DateTime? value)
    {
        if (!value.HasValue || value.Value == default)
        {
            return DateTime.UtcNow;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.Trim() ?? string.Empty;
    }

    private static bool IsCancellationStatus(string? status)
    {
        var value = NormalizeStatus(status);
        return value.Contains("取消", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "已取消", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFailureStatus(string? status)
    {
        var value = NormalizeStatus(status);
        return value.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("异常", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSkippedStatus(string? status)
    {
        return NormalizeStatus(status).Contains("跳过", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuccessfulUploadStatus(string? status)
    {
        var value = NormalizeStatus(status);
        return !string.IsNullOrWhiteSpace(value) &&
               !IsCancellationStatus(value) &&
               !IsFailureStatus(value) &&
               !IsSkippedStatus(value);
    }

    private static string BuildOrderUploadLockName(string? orderNumber)
    {
        var value = orderNumber?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $"order_upload:{value}";
    }

    private static async Task AcquireNamedLockAsync(MySqlConnection connection, string lockName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@lockName, 10);";
        command.Parameters.AddWithValue("@lockName", lockName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null || result == DBNull.Value || Convert.ToInt32(result, CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidOperationException("系统正忙，请稍后重试当前订单。");
        }
    }

    private static async Task ReleaseNamedLockAsync(MySqlConnection connection, string lockName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RELEASE_LOCK(@lockName);";
        command.Parameters.AddWithValue("@lockName", lockName);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task EnsureOrderNumberWriteAllowedAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string orderNumber,
        string incomingStatus,
        CancellationToken cancellationToken)
    {
        var statuses = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT status
                FROM order_uploads
                WHERE order_number = @orderNumber
                ORDER BY created_at_utc DESC, id DESC;
                """;
            command.Parameters.AddWithValue("@orderNumber", orderNumber.Trim());

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                statuses.Add(reader.GetString(reader.GetOrdinal("status")));
            }
        }

        var hasSuccessfulUpload = statuses.Any(IsSuccessfulUploadStatus);
        var hasSuccessfulCancellation = statuses.Any(status =>
            string.Equals(NormalizeStatus(status), "已取消", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(incomingStatus, "已取消", StringComparison.OrdinalIgnoreCase))
        {
            if (!hasSuccessfulUpload)
            {
                throw new InvalidOperationException("该订单号还没有已上传的订单记录，无法取消。");
            }

            if (hasSuccessfulCancellation)
            {
                throw new InvalidOperationException("该订单号已经取消，无需重复取消。");
            }

            return;
        }

        if (IsSuccessfulUploadStatus(incomingStatus) && hasSuccessfulUpload)
        {
            throw new InvalidOperationException("该订单号已经上传过，不能重复写入 ERP 和云端记录。");
        }
    }

    private sealed record ItemPricingRow(UploadItemCommand Item, long? PriceRuleId, string PriceName, int UnitPrice, int LineAmount);
}
