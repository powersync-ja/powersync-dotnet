namespace PowerSync.Common.Tests;

using PowerSync.Common.DB.Schema;
using PowerSync.Common.Tests.Models;

public class TestSchemaAttributes
{
    public static Table Todos = new Table(typeof(Todo));
    public static Table Lists = new Table(typeof(List));

    public static Schema AppSchema = new Schema(Todos, Lists);
}

public class TestSchemaTodoList
{
    public static Table Todos = new Table
    {
        Name = "todos",
        Columns =
        {
            ["list_id"] = ColumnType.Text,
            ["created_at"] = ColumnType.Text,
            ["completed_at"] = ColumnType.Text,
            ["description"] = ColumnType.Text,
            ["created_by"] = ColumnType.Text,
            ["completed_by"] = ColumnType.Text,
            ["completed"] = ColumnType.Integer,
        },
        Indexes =
        {
            ["list"] = ["list_id"],
            ["list_rev"] = ["-list_id"]
        }
    };

    public static Table Lists = new Table
    {
        Name = "lists",
        Columns =
        {
            ["created_at"] = ColumnType.Text,
            ["name"] = ColumnType.Text,
            ["owner_id"] = ColumnType.Text,
        }
    };

    public static readonly Schema AppSchema = new Schema(Todos, Lists);
}

public class TestSchema
{
    public static readonly Dictionary<string, ColumnType> AssetsColumns = new()
    {
        ["created_at"] = ColumnType.Text,
        ["make"] = ColumnType.Text,
        ["model"] = ColumnType.Text,
        ["serial_number"] = ColumnType.Text,
        ["quantity"] = ColumnType.Integer,
        ["user_id"] = ColumnType.Text,
        ["customer_id"] = ColumnType.Text,
        ["description"] = ColumnType.Text,
    };

    public static readonly Table Assets = new Table
    {
        Name = "assets",
        Columns = AssetsColumns,
        Indexes =
        {
            ["makemodel"] = ["make", "model"]
        }
    };

    public static readonly Table Customers = new Table
    {
        Name = "customers",
        Columns =
        {
            ["name"] = ColumnType.Text,
            ["email"] = ColumnType.Text,
        }
    };

    public static readonly Schema AppSchema = new Schema(Assets, Customers);

    public static Schema GetSchemaWithCustomAssetOptions(TableOptions? assetOptions = null)
    {
        var customAssets = new Table("assets", AssetsColumns, assetOptions);

        return new Schema(customAssets, Customers);
    }

    public static Schema MakeOptionalSyncSchema(bool synced)
    {
        string SyncedName(string name) => synced ? name : $"inactice_synced_{name}";
        string LocalName(string name) => synced ? $"inactive_local_{name}" : name;

        return new Schema(
            new Table
            {
                Name = "assets",
                Columns = AssetsColumns,
                ViewName = SyncedName("assets"),
            },
            new Table
            {
                Name = "local_assets",
                Columns = AssetsColumns,
                ViewName = LocalName("assets"),
                LocalOnly = true,
            }
        );
    }
}

