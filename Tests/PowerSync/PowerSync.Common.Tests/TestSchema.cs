namespace PowerSync.Common.Tests;

using PowerSync.Common.DB.Schema;

public class TestSchemaTodoList
{

    public static Table Todos = new Table(new Dictionary<string, ColumnType>
    {
        { "list_id", ColumnType.TEXT },
        { "created_at", ColumnType.TEXT },
        { "completed_at", ColumnType.TEXT },
        { "description", ColumnType.TEXT },
        { "created_by", ColumnType.TEXT },
        { "completed_by", ColumnType.TEXT },
        { "completed", ColumnType.INTEGER }
    }, new TableOptions
    {
        Indexes = new Dictionary<string, List<string>> { { "list", new List<string> { "list_id" } } }
    });

    public static Table Lists = new Table(new Dictionary<string, ColumnType>
    {
        { "created_at", ColumnType.TEXT },
        { "name", ColumnType.TEXT },
        { "owner_id", ColumnType.TEXT }
    });

    public static readonly Schema AppSchema = new Schema(new Dictionary<string, Table>
        {
            { "lists", Lists },
            { "todos", Todos }
        });
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
