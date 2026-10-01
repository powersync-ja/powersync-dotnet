using Newtonsoft.Json;

using PowerSync.Common.Client;
using PowerSync.Common.DB.Schema;

using PowerSync.Common.Tests.Utils;

namespace PowerSync.Common.Tests.DB;

/// <summary>
/// dotnet test -v n --framework net10.0 --filter "RawTableTests"
/// </summary>
public class RawTableTests : IAsyncLifetime
{
    private string _dbName = "";
    private PowerSyncDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _dbName = DatabaseUtils.NewDbName();
    }

    public async Task DisposeAsync()
    {
        if (_db != null)
        {
            await _db.DisconnectAndClear();
            await _db.Close();
            _db = null!;
        }
        if (_dbName != null)
        {
            DatabaseUtils.CleanDb(_dbName);
            _dbName = "";
        }
    }

    [Fact(Timeout = 5000)]
    public async Task RawTableDoesNotCreateViews()
    {
        var assets = CreateAssetsSchema();
        var schema = new Common.DB.Schema.Schema(assets);
        NewDatabase(schema);
        await CreateAssetsTable(assets);

        string tableType = (await _db.Get("SELECT type as r FROM sqlite_master WHERE name = 'assets'")).r;
        Assert.Equal("table", tableType);
        Assert.NotEqual("view", tableType);

        long powerSyncTableCount = await _db.Get<long>(@"
            SELECT count(*)
            FROM sqlite_master
            WHERE name = 'ps_data__assets'
               OR name = 'ps_data_local__assets'");
        Assert.Equal(0L, powerSyncTableCount);
    }

    [Fact(Timeout = 5000)]
    public async Task RawTableCreatesRowsInInternalTables()
    {
        var assets = CreateAssetsSchema();
        var schema = new Common.DB.Schema.Schema(assets);
        NewDatabase(schema);
        await CreateAssetsTable(assets);

        Assert.Equal(0L, await _db.Get<long>("SELECT count(*) FROM ps_crud"));
        await _db.Execute(
            "INSERT INTO assets (id, make, model) VALUES (uuid(), ?, ?)",
            ["test make", "test model"]
        );
        Assert.Equal(1L, await _db.Get<long>("SELECT count(*) FROM ps_crud"));
    }

    private static RawTable CreateAssetsSchema(RawTableSchema? customSchema = null)
    {
        return new RawTable(
            name: "assets",
            schema: customSchema ?? new RawTableSchema()
        );
    }

    private async Task CreateAssetsTable(RawTable assetsSchema)
    {
        await _db.Execute(@"
            CREATE TABLE assets (
                id            TEXT PRIMARY KEY,
                created_at    TEXT,
                make          TEXT,
                model         TEXT,
                serial_number TEXT,
                quantity      INTEGER,
                user_id       TEXT,
                customer_id   TEXT,
                description   TEXT
            );
        ");

        foreach (string action in new string[] { "INSERT", "UPDATE", "DELETE" })
        {
            await _db.Execute(
                "SELECT powersync_create_raw_table_crud_trigger(?, ?, ?)",
                [JsonConvert.SerializeObject(assetsSchema), $"assets_{action}", action]
          );
        }
    }

    private void NewDatabase(Common.DB.Schema.Schema schema)
    {
        _db = new PowerSyncDatabase(new PowerSyncDatabaseOptions()
        {
            Database = new SQLOpenOptions()
            {
                DbFilename = _dbName,
            },
            Schema = schema,
        });
    }
}
