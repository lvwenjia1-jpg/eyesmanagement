using MainApi.Options;
using MainApi.Services;
using MySqlConnector;
using Microsoft.Extensions.Options;

namespace MainApi.Data;

public sealed class DashboardSeedDataSeeder
{
    private static readonly (string LoginName, string Password, string ErpId, string Role)[] Users =
    {
        ("admin", "123456", "ERP001", "admin"),
        ("user1", "123456", "ERP002", "user"),
        ("user2", "123456", "ERP003", "user"),
        ("user3", "123456", "ERP004", "user"),
        ("user4", "123456", "ERP005", "user")
    };

    private static readonly (string Code, string Description)[] Machines =
    {
        ("MC123456", "Demo machine 1"),
        ("MC789012", "Demo machine 2"),
        ("MC345678", "Demo machine 3"),
        ("MC901234", "Demo machine 4")
    };

    private static readonly string[] GroupNames = { "Group-A", "Group-B", "Group-C", "Group-D", "Group-E", "Group-F" };
    private static readonly string[] Receivers = { "Zhang San", "Li Si", "Wang Wu", "Zhao Liu", "Chen Chen", "Liu Yang" };
    private static readonly string[] Streets =
    {
        "1009 Gubei Road, Hangzhou",
        "88 Zhangjiang Hi-Tech Road, Shanghai",
        "199 Tiyu West Road, Guangzhou",
        "8 Kejiyuan South Road, Shenzhen",
        "50 Xinghu Street, Suzhou"
    };
    private static readonly (string Code, string Name)[] Products =
    {
        ("P1001", "Daily Contact Lens"),
        ("P1002", "Monthly Contact Lens"),
        ("P1003", "Care Solution"),
        ("P1004", "Companion Lens"),
        ("P1005", "Trial Lens")
    };

    private readonly MySqlConnectionFactory _connectionFactory;
    private readonly PasswordHasher _passwordHasher;
    private readonly DashboardSeedOptions _options;

    public DashboardSeedDataSeeder(MySqlConnectionFactory connectionFactory, PasswordHasher passwordHasher, IOptions<DashboardSeedOptions> options)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _options = options.Value;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (_options.ResetExistingData)
        {
            await ClearAsync(connection, transaction, cancellationToken);
        }

        await SeedUsersAsync(connection, transaction, cancellationToken);
        await SeedMachinesAsync(connection, transaction, cancellationToken);
        await SeedBusinessGroupsAndOrdersAsync(connection, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SeedUsersAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var user in Users)
        {
            await using var existsCommand = connection.CreateCommand();
            existsCommand.Transaction = transaction;
            existsCommand.CommandText = "SELECT id FROM users WHERE login_name = @loginName LIMIT 1;";
            existsCommand.Parameters.AddWithValue("@loginName", user.LoginName);
            if (await existsCommand.ExecuteScalarAsync(cancellationToken) is not null)
            {
                continue;
            }

            var (salt, hash) = _passwordHasher.HashPassword(user.Password);
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO users (login_name, password_hash, password_salt, erp_id, role, is_active, created_at_utc)
                VALUES (@loginName, @passwordHash, @passwordSalt, @erpId, @role, 1, UTC_TIMESTAMP(6));
                """;
            insertCommand.Parameters.AddWithValue("@loginName", user.LoginName);
            insertCommand.Parameters.AddWithValue("@passwordHash", hash);
            insertCommand.Parameters.AddWithValue("@passwordSalt", salt);
            insertCommand.Parameters.AddWithValue("@erpId", user.ErpId);
            insertCommand.Parameters.AddWithValue("@role", user.Role);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task SeedMachinesAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var machine in Machines)
        {
            await using var existsCommand = connection.CreateCommand();
            existsCommand.Transaction = transaction;
            existsCommand.CommandText = "SELECT id FROM machine_codes WHERE code = @code LIMIT 1;";
            existsCommand.Parameters.AddWithValue("@code", machine.Code);
            if (await existsCommand.ExecuteScalarAsync(cancellationToken) is not null)
            {
                continue;
            }

            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO machine_codes (code, description, is_active, created_at_utc)
                VALUES (@code, @description, 1, UTC_TIMESTAMP(6));
                """;
            insertCommand.Parameters.AddWithValue("@code", machine.Code);
            insertCommand.Parameters.AddWithValue("@description", machine.Description);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task SeedBusinessGroupsAndOrdersAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = "SELECT COUNT(1) FROM business_groups;";
        if (Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken)) > 0)
        {
            return;
        }

        var random = new Random(42);
        var groupCount = Math.Clamp(_options.BusinessGroupCount, 1, GroupNames.Length);
        var ordersPerGroup = Math.Max(1, _options.OrdersPerGroup);

        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            await using var insertGroup = connection.CreateCommand();
            insertGroup.Transaction = transaction;
            insertGroup.CommandText = """
                INSERT INTO business_groups (name, balance, created_at_utc, updated_at_utc)
                VALUES (@name, @balance, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
                """;
            insertGroup.Parameters.AddWithValue("@name", GroupNames[groupIndex]);
            insertGroup.Parameters.AddWithValue("@balance", 5000m + (groupIndex * 2500m));
            await insertGroup.ExecuteNonQueryAsync(cancellationToken);
            var groupId = insertGroup.LastInsertedId;

            for (var orderIndex = 0; orderIndex < ordersPerGroup; orderIndex++)
            {
                await using var insertOrder = connection.CreateCommand();
                insertOrder.Transaction = transaction;
                insertOrder.CommandText = """
                    INSERT INTO dashboard_orders (
                        order_no,
                        business_group_id,
                        uploader_login_name,
                        receiver_name,
                        receiver_address,
                        amount,
                        tracking_number,
                        created_at_utc,
                        updated_at_utc
                    )
                    VALUES (
                        @orderNo,
                        @businessGroupId,
                        @uploaderLoginName,
                        @receiverName,
                        @receiverAddress,
                        @amount,
                        @trackingNumber,
                        UTC_TIMESTAMP(6),
                        UTC_TIMESTAMP(6)
                    );
                    """;
                insertOrder.Parameters.AddWithValue("@orderNo", $"ORD{groupIndex + 1:D2}{orderIndex + 1:D4}");
                insertOrder.Parameters.AddWithValue("@businessGroupId", groupId);
                insertOrder.Parameters.AddWithValue("@uploaderLoginName", Users[1 + ((groupIndex + orderIndex) % (Users.Length - 1))].LoginName);
                insertOrder.Parameters.AddWithValue("@receiverName", Receivers[(groupIndex + orderIndex) % Receivers.Length]);
                insertOrder.Parameters.AddWithValue("@receiverAddress", Streets[(groupIndex + orderIndex) % Streets.Length]);
                insertOrder.Parameters.AddWithValue("@amount", 99m + random.Next(50, 900));
                insertOrder.Parameters.AddWithValue("@trackingNumber", orderIndex % 3 == 0 ? string.Empty : $"YT{random.NextInt64(1000000000, 9999999999)}");
                await insertOrder.ExecuteNonQueryAsync(cancellationToken);
                var orderId = insertOrder.LastInsertedId;

                var itemCount = 1 + random.Next(0, 3);
                for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
                {
                    var product = Products[(groupIndex + orderIndex + itemIndex) % Products.Length];
                    await using var insertItem = connection.CreateCommand();
                    insertItem.Transaction = transaction;
                    insertItem.CommandText = """
                        INSERT INTO dashboard_order_items (order_id, product_code, product_name, quantity)
                        VALUES (@orderId, @productCode, @productName, @quantity);
                        """;
                    insertItem.Parameters.AddWithValue("@orderId", orderId);
                    insertItem.Parameters.AddWithValue("@productCode", product.Code);
                    insertItem.Parameters.AddWithValue("@productName", product.Name);
                    insertItem.Parameters.AddWithValue("@quantity", 1 + random.Next(0, 4));
                    await insertItem.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }
    }

    private static async Task ClearAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        var statements = new[]
        {
            "DELETE FROM dashboard_order_items;",
            "DELETE FROM dashboard_orders;",
            "DELETE FROM business_groups;",
            "DELETE FROM machine_codes;",
            "DELETE FROM users;"
        };

        foreach (var statement in statements)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
