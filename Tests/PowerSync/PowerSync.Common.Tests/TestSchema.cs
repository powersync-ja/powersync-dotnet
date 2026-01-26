namespace PowerSync.Common.Tests;

using PowerSync.Common.DB.Schema;

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

    public static readonly Table Assets = new TableBuilder()
    {
        Name = "assets",
        Columns = AssetsColumns,
        Indexes =
        {
            ["makemodel"] = ["make", "model"]
        }
    }.Build();

    public static readonly Table Customers = new TableBuilder()
    {
        Name = "customers",
        Columns =
        {
            ["name"] = ColumnType.Text,
            ["email"] = ColumnType.Text,
        }
    }.Build();

    public static readonly Schema AppSchema = new SchemaBuilder(Assets, Customers).Build();

    public static Schema GetSchemaWithCustomAssetOptions(TableOptions? assetOptions = null)
    {
        var customAssets = new Table("assets", AssetsColumns.Columns, assetOptions);

        return new SchemaBuilder(customAssets, Customers).Build();
    }
}
