namespace PowerSync.Common.Tests;

using PowerSync.Common.DB.Schema;

public class TestSchemaTodoList
{
    public static TableFactory Todos = new TableFactory()
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
            ["list"] = ["list_id"]
        }
    };

    public static TableFactory Lists = new TableFactory()
    {
        Name = "lists",
        Columns =
        {
            ["created_at"] = ColumnType.Text,
            ["name"] = ColumnType.Text,
            ["owner_id"] = ColumnType.Text,
        }
    };

    public static readonly Schema AppSchema = new SchemaFactory(Todos, Lists).Create();
}

public class TestSchema
{
    public static readonly ColumnMap AssetsColumns = new ColumnMap
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

    public static readonly Table Assets = new TableFactory()
    {
        Name = "assets",
        Columns = AssetsColumns,
        Indexes =
        {
            ["makemodel"] = ["make", "model"]
        }
    }.Create();

    public static readonly Table Customers = new TableFactory()
    {
        Name = "customers",
        Columns =
        {
            ["name"] = ColumnType.Text,
            ["email"] = ColumnType.Text,
        }
    }.Create();

    public static readonly Schema AppSchema = new SchemaFactory(Assets, Customers).Create();

    public static Schema GetSchemaWithCustomAssetOptions(TableOptions? assetOptions = null)
    {
        var customAssets = new Table("assets", AssetsColumns.Columns, assetOptions);

        return new SchemaFactory(customAssets, Customers).Create();
    }
}
