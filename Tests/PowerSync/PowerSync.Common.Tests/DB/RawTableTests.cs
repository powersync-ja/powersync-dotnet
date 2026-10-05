using Newtonsoft.Json;

using PowerSync.Common.Client;
using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.Client.Sync.Stream;
using PowerSync.Common.DB.Crud;
using PowerSync.Common.DB.Schema;

using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Tests.Utils.Sync;

namespace PowerSync.Common.Tests.DB;

/// <summary>
/// dotnet test -v n --framework net10.0 --filter "RawTableTests"
/// </summary>
public class RawTableTests : IAsyncLifetime
{
    private string _dbName = "";
    private PowerSyncDatabase _db = null!;
    private MockSyncService? _syncService;

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
        _syncService?.Close();
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

    [Fact]
    public void Schema_SerializesRawTablesToJSON()
    {
        var schema = new Common.DB.Schema.Schema(
            new RawTable(
                name: "lists",
                put: new PendingStatement("SELECT 1", [PendingStatementParameter.Column("foo"), PendingStatementParameter.Rest]),
                delete: new PendingStatement("SELECT 2", [PendingStatementParameter.Id])
            ),
            new RawTable(
                name: "sync_name",
                schema: new RawTableSchema
                {
                    TableName = "users",
                    SyncedColumns = ["name"],
                    Options = new TableOptions
                    {
                        IgnoreEmptyUpdates = true,
                        TrackPreviousValues = new TrackPreviousOptions(),
                    }
                },
                clear: "DELETE FROM users"
            )
        );

        object expectedJson = new
        {
            tables = new List<object>(),
            raw_tables = new List<object>
            {
                new
                {
                    name = "lists",
                    put = new
                    {
                        sql = "SELECT 1",
                        @params = new List<object> { new { Column = "foo" }, "Rest" }
                    },
                    delete = new
                    {
                        sql = "SELECT 2",
                        @params = new List<object> { "Id" }
                    }
                },
                new
                {
                    name = "sync_name",
                    clear = "DELETE FROM users",
                    table_name = "users",
                    synced_columns = new List<string> { "name" },
                    local_only = false,
                    insert_only = false,
                    ignore_empty_update = true,
                    include_metadata = false,
                    include_old = true,
                    include_old_only_when_changed = false
                }
            }
        };

        Assert.Equal(JsonConvert.SerializeObject(expectedJson), JsonConvert.SerializeObject(schema));
    }

    [Fact(Timeout = 5000)]
    public async Task InferredCrudTrigger()
    {
        var table = new RawTable("users", new RawTableSchema());
        NewDatabase(new Common.DB.Schema.Schema(table));

        await _db.Execute("CREATE TABLE users (id TEXT, name TEXT);");
        await CreateTrigger(table, "users_insert", "INSERT");

        await _db.Execute("INSERT INTO users (id, name) VALUES (?, ?);", ["id", "user"]);

        var tx = await _db.GetNextCrudTransaction();
        Assert.NotNull(tx);
        var write = Assert.Single(tx.Crud);
        Assert.Equal(UpdateType.PUT, write.Op);
        Assert.Equal("users", write.Table);
        Assert.Equal("id", write.Id);
        Assert.Equal(new Dictionary<string, object> { ["name"] = "user" }, write.OpData);
    }

    [Fact(Timeout = 5000)]
    public async Task CrudTriggerWithOptions()
    {
        var table = new RawTable(
            name: "sync_name",
            schema: new RawTableSchema
            {
                TableName = "users",
                SyncedColumns = ["name"],
                Options = new TableOptions
                {
                    IgnoreEmptyUpdates = true,
                    TrackPreviousValues = new TrackPreviousOptions(),
                }
            }
        );
        NewDatabase(new Common.DB.Schema.Schema(table));

        await _db.Execute("CREATE TABLE users (id TEXT, name TEXT, local TEXT);");
        await _db.Execute("INSERT INTO users (id, name, local) VALUES (?, ?, ?);", ["id", "name", "local"]);
        await CreateTrigger(table, "users_update", "UPDATE");

        await _db.Execute("UPDATE users SET name = ?, local = ?;", ["updated_name", "updated_local"]);
        // This should not generate a CRUD entry because the only synced column is not affected.
        await _db.Execute("UPDATE users SET name = ?, local = ?;", ["updated_name", "updated_local_2"]);

        var tx = await _db.GetNextCrudTransaction();
        Assert.NotNull(tx);
        var write = Assert.Single(tx.Crud);
        Assert.Equal(UpdateType.PATCH, write.Op);
        Assert.Equal("sync_name", write.Table);
        Assert.Equal("id", write.Id);
        // These should not include the local-only column
        Assert.Equal(new Dictionary<string, object> { ["name"] = "updated_name" }, write.OpData);
        Assert.Equal(new Dictionary<string, string?> { ["name"] = "name" }, write.PreviousValues);
    }

    [Fact(Timeout = 5000)]
    public async Task DisconnectAndClearRunsClearStatement()
    {
        NewDatabase(new Common.DB.Schema.Schema(
            new RawTable("users", new RawTableSchema { TableName = "lists" }, clear: "DELETE FROM lists")
        ));

        await _db.Execute("CREATE TABLE lists (id TEXT NOT NULL PRIMARY KEY, name TEXT)");
        await _db.Execute("INSERT INTO lists (id, name) VALUES (uuid(), ?)", ["list"]);

        Assert.Single(await _db.GetAll<ListRow>("SELECT * FROM lists"));
        await _db.DisconnectAndClear();
        Assert.Empty(await _db.GetAll<ListRow>("SELECT * FROM lists"));
    }

    [Fact(Timeout = 15000)]
    public async Task SyncWithInferredStatements()
    {
        NewSyncedDatabase(new Common.DB.Schema.Schema(
            new RawTable("lists", new RawTableSchema())
        ));
        await _db.Execute("CREATE TABLE lists (id TEXT NOT NULL PRIMARY KEY, name TEXT);");

        var query = _db.Watch<ListRow>("SELECT * FROM lists", null, new() { TriggerImmediately = true }).GetAsyncEnumerator();
        await query.MoveNextAsync();
        Assert.Empty(query.Current);

        await _db.Connect(new TestConnector());

        PushPut(opId: 1, data: """{"name": "custom list"}""");
        await _db.WaitForFirstSync();

        await query.MoveNextAsync();
        var row = Assert.Single(query.Current);
        Assert.Equal("my_list", row.id);
        Assert.Equal("custom list", row.name);

        PushRemove(opId: 2);

        await query.MoveNextAsync();
        Assert.Empty(query.Current);
    }

    [Fact(Timeout = 15000)]
    public async Task SyncWithExplicitStatements()
    {
        NewSyncedDatabase(new Common.DB.Schema.Schema(
            new RawTable(
                name: "lists",
                put: new PendingStatement(
                    "INSERT OR REPLACE INTO lists (id, name, _rest) VALUES (?, ?, ?)",
                    [PendingStatementParameter.Id, PendingStatementParameter.Column("name"), PendingStatementParameter.Rest]
                ),
                delete: new PendingStatement(
                    "DELETE FROM lists WHERE id = ?",
                    [PendingStatementParameter.Id]
                )
            )
        ));
        await _db.Execute("CREATE TABLE lists (id TEXT NOT NULL PRIMARY KEY, name TEXT, _rest TEXT);");

        var query = _db.Watch<ListRow>("SELECT * FROM lists", null, new() { TriggerImmediately = true }).GetAsyncEnumerator();
        await query.MoveNextAsync();
        Assert.Empty(query.Current);

        await _db.Connect(new TestConnector());

        PushPut(opId: 1, data: """{"name": "custom list", "additional": "foo"}""");
        await _db.WaitForFirstSync();

        await query.MoveNextAsync();
        var row = Assert.Single(query.Current);
        Assert.Equal("my_list", row.id);
        Assert.Equal("custom list", row.name);
        Assert.Equal("""{"additional":"foo"}""", row._rest);

        PushRemove(opId: 2);

        await query.MoveNextAsync();
        Assert.Empty(query.Current);
    }

    private class ListRow
    {
        public string id { get; set; } = "";
        public string? name { get; set; }
        public string? _rest { get; set; }
    }

    private void PushPut(long opId, string data)
    {
        PushOperation(opId, new OplogEntryJSON
        {
            Checksum = 0,
            OpId = opId,
            Op = "PUT",
            ObjectId = "my_list",
            ObjectType = "lists",
            Data = data,
        });
    }

    private void PushRemove(long opId)
    {
        PushOperation(opId, new OplogEntryJSON
        {
            Checksum = 0,
            OpId = opId,
            Op = "REMOVE",
            ObjectId = "my_list",
            ObjectType = "lists",
        });
    }

    private void PushOperation(long opId, OplogEntryJSON entry)
    {
        _syncService!.PushLine(MockDataFactory.Checkpoint(opId, [MockDataFactory.Bucket("a", (int)opId, subscriptions: Array.Empty<object>())]));
        _syncService.PushLine(new StreamingSyncDataJSON
        {
            Data = new SyncDataBucketJSON
            {
                Bucket = "a",
                Data = [entry]
            }
        });
        _syncService.PushLine(MockDataFactory.CheckpointComplete(opId.ToString()));
    }

    private async Task CreateTrigger(RawTable table, string name, string write)
    {
        await _db.Execute(
            "SELECT powersync_create_raw_table_crud_trigger(?, ?, ?)",
            [table.JsonDescription(), name, write]
        );
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
            await CreateTrigger(assetsSchema, $"assets_{action}", action);
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

    private void NewSyncedDatabase(Common.DB.Schema.Schema schema)
    {
        _syncService = new MockSyncService();
        _db = _syncService.CreateDatabase(dbFilename: _dbName, schema: schema);
    }
}
