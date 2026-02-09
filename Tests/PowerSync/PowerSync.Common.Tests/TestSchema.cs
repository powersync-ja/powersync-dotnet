namespace PowerSync.Common.Tests;

using PowerSync.Common.DB.Schema;
using PowerSync.Common.Tests.Models;

public class TestSchemaTodoList
{
    public static Table Todos = new Table(typeof(Todo));
    public static Table Lists = new Table(typeof(List));

    public static readonly Schema AppSchema = new Schema(Todos, Lists);
}

public class TestSchema
{
    public static readonly Table Assets = new Table(typeof(Asset));
    public static readonly Table Customers = new Table(typeof(Customer));

    public static readonly Schema AppSchema = new Schema(Assets, Customers);

    public static Schema GetSchemaWithCustomAssetOptions(TableOptions? assetOptions = null)
    {
        var customAssets = new Table(Assets, assetOptions);
        return new Schema(customAssets, Customers);
    }
}
