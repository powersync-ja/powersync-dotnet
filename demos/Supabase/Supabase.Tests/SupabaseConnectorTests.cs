using Common.Client.Sync.Bucket;
using Common.Client.Sync.Stream;
using Common.DB.Schema;
using Common.MicrosoftDataSqlite;
using Newtonsoft.Json;
using Supabase.Storage;

namespace Supabase.Tests;
class TestData
{
    public static Table todos = new Table(new Dictionary<string, ColumnType>
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

    public static Table lists = new Table(new Dictionary<string, ColumnType>
{
    { "created_at", ColumnType.TEXT },
    { "name", ColumnType.TEXT },
    { "owner_id", ColumnType.TEXT }
});

    public static Schema appSchema = new Schema(new Dictionary<string, Table>
{
    { "todos", todos },
    { "lists", lists }
});


}


public class SupabaseConnectorTests
{
    // [Fact]
    // public async void Connector()
    // {
    //     Console.WriteLine("Supabase Connector Test");
    //     new SupabaseConnector();
    // }

    [Fact]
    public async void StreamTest()
    {
        var db = CommonPowerSyncDatabase.Create(TestData.appSchema, "ConnectorTests.db");
        await db.Init();
        await db.DisconnectAndClear();

        var connector = new SupabaseConnector();
        await connector.Login();

        Console.WriteLine("Calling connect...");

        await db.Connect(connector);

        // var syncImplementation = new StreamingSyncImplementation(new StreamingSyncImplementationOptions
        // {
        //     Adapter = bucketStorage,
        //     Remote = remote,
        //     UploadCrud = () => Task.CompletedTask,
        // });

        // _ = syncImplementation.Connect();

        await Task.Delay(5000);

        var b = await db.Execute("SELECT * from lists");
        string jsona = JsonConvert.SerializeObject(b.Rows.Array, Formatting.Indented);
        Console.WriteLine("Lists: " + jsona);


        // await foreach (var item in remote.PostStream(syncOptions))
        // {
        //     // Console.WriteLine($"Parsed object type: {item.GetType().Name}");
        //     Console.WriteLine(JsonConvert.SerializeObject(item, Formatting.Indented));
        // }
        Console.WriteLine("XXXX---completed");
    }
}