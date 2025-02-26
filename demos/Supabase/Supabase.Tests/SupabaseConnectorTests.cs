using Common.Client.Sync.Bucket;
using Common.Client.Sync.Stream;
using Common.DB.Schema;
using Common.MicrosoftDataSqlite;
using Microsoft.Extensions.Logging;
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
        var db = CommonPowerSyncDatabase.Create(TestData.appSchema, "ConnectorTests.db", createLogger());
        await db.Init();
        await db.DisconnectAndClear();

        var connector = new SupabaseConnector();
        await connector.Login();

        Console.WriteLine("Calling connect...");
        // db.OnChange(new Common.Client.WatchOnChangeHandler
        // {
        //     OnChange = async (change) =>
        //     {
        //         Console.WriteLine("Change: " + JsonConvert.SerializeObject(change, Formatting.Indented));
        //     }
        // }, new Common.Client.SQLWatchOptions
        // {
        //     Tables = ["lists"]
        // });
        // var x = await db.ResolveTables("select * from lists join todos on lists.id = todos.list_id");
        // Console.WriteLine("Tables: " + JsonConvert.SerializeObject(x, Formatting.Indented));


        db.Watch("select * from lists", null, new Common.Client.WatchHandler
        {
            OnResult = async (result) =>
            {
                Console.WriteLine("\n\nResult from watch:" + JsonConvert.SerializeObject(result, Formatting.Indented));
            },
        });

        await db.Execute("INSERT INTO lists (id, name, owner_id, created_at) VALUES ('1', 'test', 'test', 'test')");
        await Task.Delay(1000);
        await db.Execute("INSERT INTO lists (id, name, owner_id, created_at) VALUES ('2', 'test2', 'test2', 'test2')");


        // string jsona = JsonConvert.SerializeObject(b, Formatting.Indented);
        // Console.WriteLine("Lists: " + jsona);

    }

    private ILogger createLogger()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(); // Enable console logging
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        return loggerFactory.CreateLogger("TestLogger");
    }

}