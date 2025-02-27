namespace Node.Tests;

using Common.DB.Schema;
using Common.MicrosoftDataSqlite;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

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


public class NodeConnectorTests
{


    // [Fact(Skip = "skip")]
    [Fact]
    public async void StreamTest()
    {
        var db = CommonPowerSyncDatabase.Create(TestData.appSchema, "ConnectorTests.db", createLogger());
        await db.Init();
        await db.DisconnectAndClear();

        var connector = new NodeConnector();


        Console.WriteLine("Calling connect!...");
        await db.Connect(connector);
        await Task.Delay(4000);

        db.Watch("select * from lists", null, new Common.Client.WatchHandler
        {
            OnResult = async (result) =>
            {
                Console.WriteLine();
                Console.WriteLine("X: Result from watch:" + JsonConvert.SerializeObject(result, Formatting.Indented));
            },
        });

        var ownerId = connector.UserId;
        // Console.WriteLine(owner_id);
        await db.Execute("INSERT INTO lists (id, name, owner_id, created_at) VALUES (uuid(), 'Example 5 with Watch enabled', ?, datetime())", [ownerId]);
        await Task.Delay(4000);
        // await db.Execute("INSERT INTO lists (id, name, owner_id, created_at) VALUES ('2', 'test2', 'test2', 'test2')");


        var x = await db.GetAll<object>("select * from lists ");
        string jsona = JsonConvert.SerializeObject(x, Formatting.Indented);
        // Console.WriteLine("Lists from node: " + jsona);
        // Console.WriteLine("--------------");
        await Task.Delay(15000);
        // Console.WriteLine("Lists: " + jsona);

    }

    // [Fact]
    [Fact(Skip = "skip")]
    public async void ReadTransaction()
    {
        Console.WriteLine("ReadTransaction");

        var db = CommonPowerSyncDatabase.Create(TestData.appSchema, "ConnectorTests.db", createLogger());
        await db.Init();
        await db.DisconnectAndClear();

        try
        {
            await db.Database.ReadTransaction(async (ctx) =>
            {
                Console.WriteLine("Readtransaction executing!");
                await Task.CompletedTask;
                return 5;
            });
        }
        catch (Exception e)
        {
            Console.WriteLine("BROKEN!: " + e.Message);
        }

        // await db.Database.ReadLock(async (ctx) =>
        // {
        //     Console.WriteLine("ReadLock");
        //     await Task.CompletedTask;
        //     return 5;
        // });
    }



    private ILogger createLogger()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(); // Enable console logging
            builder.SetMinimumLevel(LogLevel.Information);
        });

        return loggerFactory.CreateLogger("TestLogger");
    }

}