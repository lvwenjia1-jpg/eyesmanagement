using System.Globalization;
using MainApi.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace MainApi.Data;

public sealed class MockOrderDataSeeder
{
    private static readonly string[] MachineCodes = { "DEMO-PC-001", "DEMO-PC-002", "DEMO-PC-003" };
    private static readonly string[] LoginNames = { "admin", "operator01", "operator02", "auditor01" };
    private static readonly string[] DisplayNames = { "系统管理员", "一号录单员", "二号录单员", "复核员" };
    private static readonly string[] ErpIds = { "ERP001", "ERP002", "ERP003", "ERP004" };
    private static readonly string[] WecomIds = { "wecom_admin", "wecom_01", "wecom_02", "wecom_03" };
    private static readonly string[] ReceiverNames = { "张三", "李四", "王五", "赵六", "陈晨", "刘洋", "孙敏" };
    private static readonly string[] Cities = { "上海市浦东新区", "杭州市西湖区", "广州市天河区", "深圳市南山区", "苏州市工业园区" };
    private static readonly string[] Streets = { "星海路88号", "中山路188号", "建国路66号", "人民路520号", "软件大道99号" };
    private static readonly string[] Statuses = { "已接收", "待审核", "已上传", "已完成" };
    private static readonly (string Code, string Name)[] Products =
    {
        ("P1001", "日抛隐形眼镜"),
        ("P1002", "月抛隐形眼镜"),
        ("P1003", "护理液"),
        ("P1004", "伴侣盒"),
        ("P1005", "试戴片")
    };
    private static readonly string[] Degrees = { "0.00", "-1.25", "-2.00", "-3.50", "-4.75", "-6.00" };
    private static readonly string[] WearPeriods = { "日抛", "双周抛", "月抛", "季抛" };

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly MockOrderSeedOptions _options;

    public MockOrderDataSeeder(SqliteConnectionFactory connectionFactory, IOptions<MockOrderSeedOptions> options)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var days = Math.Max(1, _options.Days);
        var ordersPerDay = Math.Max(1, _options.OrdersPerDay);
        var maxItemsPerOrder = Math.Max(1, _options.MaxItemsPerOrder);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var existingCount = await CountUploadsAsync(connection, cancellationToken);
        if (existingCount > 0 && _options.SkipWhenUploadsExist && !_options.ResetExistingUploads)
        {
            return;
        }

        await using var transaction = connection.BeginTransaction();

        if (_options.ResetExistingUploads)
        {
            await ClearUploadsAsync(connection, transaction, cancellationToken);
        }

        await using var uploadCommand = CreateUploadInsertCommand(connection, transaction);
        await using var itemCommand = CreateItemInsertCommand(connection, transaction);

        var random = new Random();
        var sequence = 1;
        for (var dayOffset = days - 1; dayOffset >= 0; dayOffset--)
        {
            var day = DateTime.UtcNow.Date.AddDays(-dayOffset);
            for (var orderIndex = 0; orderIndex < ordersPerDay; orderIndex++)
            {
                var createdAtUtc = day.AddSeconds(random.Next(0, 24 * 60 * 60));
                var createdOn = ToDateKey(createdAtUtc);
                var machineIndex = random.Next(MachineCodes.Length);
                var userIndex = random.Next(LoginNames.Length);
                var receiverIndex = random.Next(ReceiverNames.Length);
                var itemCount = random.Next(1, maxItemsPerOrder + 1);
                var orderNumber = $"MOCK{createdOn}{sequence:D6}";
                var uploadNo = $"UP{createdOn}{sequence:D6}";

                BindUploadCommand(
                    uploadCommand,
                    uploadNo,
                    $"DRAFT-{createdOn}-{sequence:D6}",
                    orderNumber,
                    $"SESSION-{createdOn}-{sequence:D4}",
                    LoginNames[userIndex],
                    DisplayNames[userIndex],
                    ErpIds[userIndex],
                    WecomIds[userIndex],
                    MachineCodes[machineIndex],
                    ReceiverNames[receiverIndex],
                    BuildPhoneNumber(random),
                    $"{Cities[random.Next(Cities.Length)]}{Streets[random.Next(Streets.Length)]}",
                    random.Next(100) < 15 ? "含赠品" : string.Empty,
                    random.Next(100) < 25,
                    Statuses[random.Next(Statuses.Length)],
                    "mock 数据自动生成",
                    "{\"source\":\"mock-seeder\"}",
                    "{\"result\":\"ok\"}",
                    itemCount,
                    createdOn,
                    createdAtUtc);

                var uploadId = Convert.ToInt64(await uploadCommand.ExecuteScalarAsync(cancellationToken));

                for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
                {
                    var product = Products[random.Next(Products.Length)];
                    BindItemCommand(
                        itemCommand,
                        uploadId,
                        $"订单 {orderNumber} 商品 {itemIndex + 1}",
                        product.Code,
                        product.Name,
                        random.Next(1, 4),
                        Degrees[random.Next(Degrees.Length)],
                        WearPeriods[random.Next(WearPeriods.Length)],
                        random.Next(100) < 10 ? "试戴备注" : string.Empty,
                        product.Code == "P1005");
                    await itemCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                sequence++;
            }
        }

        await using (var analyze = connection.CreateCommand())
        {
            analyze.Transaction = transaction;
            analyze.CommandText = "ANALYZE;";
            await analyze.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<long> CountUploadsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM order_uploads;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ClearUploadsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM order_upload_items;
            DELETE FROM order_uploads;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand CreateUploadInsertCommand(SqliteConnection connection, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
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

        AddParameter(command, "$uploadNo");
        AddParameter(command, "$draftId");
        AddParameter(command, "$orderNumber");
        AddParameter(command, "$sessionId");
        AddParameter(command, "$uploaderLoginName");
        AddParameter(command, "$uploaderDisplayName");
        AddParameter(command, "$uploaderErpId");
        AddParameter(command, "$uploaderWecomId");
        AddParameter(command, "$machineCode");
        AddParameter(command, "$receiverName");
        AddParameter(command, "$receiverMobile");
        AddParameter(command, "$receiverAddress");
        AddParameter(command, "$remark");
        AddParameter(command, "$hasGift");
        AddParameter(command, "$status");
        AddParameter(command, "$statusDetail");
        AddParameter(command, "$externalRequestJson");
        AddParameter(command, "$externalResponseJson");
        AddParameter(command, "$itemCount");
        AddParameter(command, "$createdOn");
        AddParameter(command, "$createdAtUtc");
        AddParameter(command, "$updatedAtUtc");
        return command;
    }

    private static SqliteCommand CreateItemInsertCommand(SqliteConnection connection, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
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

        AddParameter(command, "$orderUploadId");
        AddParameter(command, "$sourceText");
        AddParameter(command, "$productCode");
        AddParameter(command, "$productName");
        AddParameter(command, "$quantity");
        AddParameter(command, "$degreeText");
        AddParameter(command, "$wearPeriod");
        AddParameter(command, "$remark");
        AddParameter(command, "$isTrial");
        return command;
    }

    private static void BindUploadCommand(
        SqliteCommand command,
        string uploadNo,
        string draftId,
        string orderNumber,
        string sessionId,
        string uploaderLoginName,
        string uploaderDisplayName,
        string uploaderErpId,
        string uploaderWecomId,
        string machineCode,
        string receiverName,
        string receiverMobile,
        string receiverAddress,
        string remark,
        bool hasGift,
        string status,
        string statusDetail,
        string externalRequestJson,
        string externalResponseJson,
        int itemCount,
        int createdOn,
        DateTime createdAtUtc)
    {
        command.Parameters["$uploadNo"].Value = uploadNo;
        command.Parameters["$draftId"].Value = draftId;
        command.Parameters["$orderNumber"].Value = orderNumber;
        command.Parameters["$sessionId"].Value = sessionId;
        command.Parameters["$uploaderLoginName"].Value = uploaderLoginName;
        command.Parameters["$uploaderDisplayName"].Value = uploaderDisplayName;
        command.Parameters["$uploaderErpId"].Value = uploaderErpId;
        command.Parameters["$uploaderWecomId"].Value = uploaderWecomId;
        command.Parameters["$machineCode"].Value = machineCode;
        command.Parameters["$receiverName"].Value = receiverName;
        command.Parameters["$receiverMobile"].Value = receiverMobile;
        command.Parameters["$receiverAddress"].Value = receiverAddress;
        command.Parameters["$remark"].Value = remark;
        command.Parameters["$hasGift"].Value = hasGift ? 1 : 0;
        command.Parameters["$status"].Value = status;
        command.Parameters["$statusDetail"].Value = statusDetail;
        command.Parameters["$externalRequestJson"].Value = externalRequestJson;
        command.Parameters["$externalResponseJson"].Value = externalResponseJson;
        command.Parameters["$itemCount"].Value = itemCount;
        command.Parameters["$createdOn"].Value = createdOn;
        command.Parameters["$createdAtUtc"].Value = FormatDate(createdAtUtc);
        command.Parameters["$updatedAtUtc"].Value = FormatDate(createdAtUtc);
    }

    private static void BindItemCommand(
        SqliteCommand command,
        long uploadId,
        string sourceText,
        string productCode,
        string productName,
        int quantity,
        string degreeText,
        string wearPeriod,
        string remark,
        bool isTrial)
    {
        command.Parameters["$orderUploadId"].Value = uploadId;
        command.Parameters["$sourceText"].Value = sourceText;
        command.Parameters["$productCode"].Value = productCode;
        command.Parameters["$productName"].Value = productName;
        command.Parameters["$quantity"].Value = quantity;
        command.Parameters["$degreeText"].Value = degreeText;
        command.Parameters["$wearPeriod"].Value = wearPeriod;
        command.Parameters["$remark"].Value = remark;
        command.Parameters["$isTrial"].Value = isTrial ? 1 : 0;
    }

    private static void AddParameter(SqliteCommand command, string name)
    {
        command.Parameters.Add(new SqliteParameter(name, DBNull.Value));
    }

    private static int ToDateKey(DateTime value)
    {
        return int.Parse(value.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string BuildPhoneNumber(Random random)
    {
        return $"13{random.Next(100000000, 999999999)}";
    }
}
