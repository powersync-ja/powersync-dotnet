namespace Common.Tests;


using Common.Client.Connection;
using Common.DB.Schema;
using Common.MicrosoftDataSqlite;
using Newtonsoft.Json;
using SQLite;

public class PowerSyncCredentialsTests
{
    private Schema AppSchema;
    public PowerSyncCredentialsTests()
    {
        var users = new Table(new Dictionary<string, ColumnType>
        {
            { "name", ColumnType.TEXT },
            { "age", ColumnType.INTEGER }
        });

        var posts = new Table(new Dictionary<string, ColumnType>
        {
            { "title", ColumnType.TEXT },
            { "content", ColumnType.TEXT }
        });

        AppSchema = new Schema(new Dictionary<string, Table>
        {
            { "users", users },
            { "posts", posts }
        });
    }

    [Fact(Skip = "Skipping this test temporarily")]
    public void SimpleTest()
    {
        var endpoint = "http://localhost";
        var token = "token";
        var expiresAt = new DateTime();
        PowerSyncCredentials credentials = new PowerSyncCredentials(endpoint, token, expiresAt);
        Assert.Equal(endpoint, credentials.Endpoint);
        Assert.Equal(token, credentials.Token);
        Assert.Equal(expiresAt, credentials.ExpiresAt);
    }

    [Fact(Skip = "Skipping this test temporarily")]
    public async void LoadVersion()
    {
        // var db = new MDSAdapter();
        var db = CommonPowerSyncDatabase.Create(AppSchema, "x.db");
        Console.WriteLine("Pre adapter" + db.SdkVersion);
        await db.WaitForReady();
        Console.WriteLine("Post adapter" + db.SdkVersion);

        await db.Execute(@"CREATE TABLE Users (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Name TEXT NOT NULL
        );");

        await db.Execute(@"INSERT INTO Users (Name) VALUES ('Alice');");
        await db.Execute(@"INSERT INTO Users (Name) VALUES ('Bob');");
        await db.Execute(@"UPDATE USERS set Name = 'Wonderland' where Name = 'Alice';");

        var x = await db.Execute("SELECT Name FROM Users limit 1;", []);

        string json = JsonConvert.SerializeObject(x.Rows.Array, Formatting.Indented);
        Console.WriteLine("Result: " + json);
        // var x = await db.Execute("SELECT powersync_rs_version() as version");
        // Console.WriteLine(x.Rows.Array.First().First());

        // var x = await db.Execute("SELECT powersync_rs_version() as version");
        // using var connection = new SqliteConnection("Data Source=:memory:");
        // var db = new MDSConnection(new MDSConnectionOptions(connection));
        // connection.Open();

        // string extensionPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../libpowersync.dylib");

        // connection.LoadExtension(extensionPath);

        // var x = await db.Execute("SELECT powersync_rs_version() as version where 1 = 0;");
        // var x = await db.Execute("SELECT * FROM Users WHERE 1 = 0;");


        // Console.WriteLine(x.Rows.Array.First().First().Value);
        // new AbstractPowerSyncDatabase();
        // await Task.Delay(5000);
    }

    [Fact(Skip = "Skipping this test temporarily")]
    public async void SchemaTest()
    {
        var db = CommonPowerSyncDatabase.Create(AppSchema, "xxxa.db");
        await db.DisconnectAndClear();
        // const schema = new Schema({
        //   users: new Table({
        //     name: column.text,
        //     age: { type: ColumnType.INTEGER }
        //   }),
        //   posts: new Table({
        //     title: column.text,
        //     content: column.text
        //   })
        // });


        var x = await db.Execute("SELECT name, sql FROM sqlite_master WHERE type='table' ORDER BY name;");
        string json = JsonConvert.SerializeObject(x.Rows.Array, Formatting.Indented);
        // Console.WriteLine("Result: " + json);
        await db.Execute(@"INSERT INTO users (id, name, age) VALUES ('1','Alice', 20);");

        var b = await db.Execute("SELECT * from users");
        string jsona = JsonConvert.SerializeObject(b.Rows.Array, Formatting.Indented);

        Console.WriteLine("Result xxx: " + jsona);

        // var c = await db.Execute("PRAGMA table_info(users);");
        // string jsonb = JsonConvert.SerializeObject(c.Rows.Array, Formatting.Indented);

        // var k = await db.Database.ReadTransaction(async (tx) =>
        // {
        //     Console.WriteLine("reee");

        //     return await tx.Execute("select * from users limit 1");
        // });
        // string jsonb = JsonConvert.SerializeObject(k.Rows.Array, Formatting.Indented);

        // Console.WriteLine(jsonb);
        // 

        // Console.WriteLine(AppSchema.ToJson());
    }

}