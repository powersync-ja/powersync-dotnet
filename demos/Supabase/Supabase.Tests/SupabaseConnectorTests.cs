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

        await db.Connect(connector);
        await Task.Delay(3000);

        var b = await db.GetAll<object>("SELECT * from lists");

        // proof data has synced
        string jsona = JsonConvert.SerializeObject(b, Formatting.Indented);
        Console.WriteLine("Lists: " + jsona);
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