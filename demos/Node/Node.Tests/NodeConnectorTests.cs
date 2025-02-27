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


    [Fact]
    public async void StreamTest()
    {
        var db = CommonPowerSyncDatabase.Create(TestData.appSchema, "ConnectorTests.db", createLogger());
        await db.Init();
        await db.DisconnectAndClear();

        var connector = new NodeConnector();


        Console.WriteLine("Calling connect...");
        await db.Connect(connector);
        await Task.Delay(4000);

        var ownerId = connector.UserId;
        // Console.WriteLine(owner_id);
        await db.Execute("INSERT INTO lists (id, name, owner_id, created_at) VALUES (uuid(), 'Example 2', ?, datetime())", [ownerId]);
        await Task.Delay(4000);
        // await db.Execute("INSERT INTO lists (id, name, owner_id, created_at) VALUES ('2', 'test2', 'test2', 'test2')");


        var x = await db.GetAll<object>("select * from lists ");
        string jsona = JsonConvert.SerializeObject(x, Formatting.Indented);
        Console.WriteLine("Lists from node: " + jsona);
        // Console.WriteLine("Lists: " + jsona);

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