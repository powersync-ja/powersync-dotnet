using Newtonsoft.Json;

using PowerSync.Common.Client;
using PowerSync.Common.DB.Schema;

using PowerSync.Common.Tests.Utils;

namespace PowerSync.Common.Tests.DB;

/// <summary>
/// dotnet test -v n --framework net10.0 --filter "SchemaTests"
/// </summary>
public class RawTableTests
{
    [Fact(Timeout = 5000)]
    public async Task RawTablesDoesNotCreateViews()
    {
        // TODO Move assets table out of just this function
        var assets = new RawTable(
            name: "assets",
            schema: new RawTableSchema()
        );
        var schema = new Common.DB.Schema.Schema(assets);

        var dbName = DatabaseUtils.NewDbName();
        var db = NewDatabase(dbName, schema);

        try
        {
            await db.Execute(@"
                CREATE TABLE assets (
                    id            TEXT PRIMARY KEY,
                    created_at    TEXT,
                    make          TEXT,
                    model         TEXT,
                    serial_number TEXT,
                    quantity      INTEGER,
                    user_id       TEXT,
                    customer_id   TEXT,
                    description   TEXT,
                );
            ");

            foreach (string action in new string[] { "INSERT", "UPDATE", "DELETE" })
            {
                await db.Execute(
                    "SELECT powersync_create_raw_table_crud_trigger(?, ?, ?)",
                    [JsonConvert.SerializeObject(assets), $"assets_{action}", action]
              );
            }

            string tableType = await db.Get("SELECT type as r FROM sqlite_master WHERE name = 'assets'");
            Assert.Equal("table", tableType);
            Assert.NotEqual("view", tableType);

            int powerSyncTableCount = await db.Get(@"
                SELECT count(*) as r
                FROM sqlite_master
                WHERE name = 'ps_data__assets'
                   OR name = 'ps_data_local__assets'");
            Assert.Equal(0, powerSyncTableCount);
        }
        finally
        {
            await db.DisconnectAndClear();
            await db.Close();
            DatabaseUtils.CleanDb(dbName);
        }
    }

    private static PowerSyncDatabase NewDatabase(string name, Common.DB.Schema.Schema schema)
    {
        return new PowerSyncDatabase(new PowerSyncDatabaseOptions()
        {
            Database = new SQLOpenOptions()
            {
                DbFilename = name,
            },
            Schema = schema,
        });
    }
}
