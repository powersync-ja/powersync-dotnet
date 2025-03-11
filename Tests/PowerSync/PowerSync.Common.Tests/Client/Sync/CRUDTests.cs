namespace PowerSync.Common.Tests.Client;

using System.Diagnostics;
using Newtonsoft.Json;
using PowerSync.Common.Client;
using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.DB.Crud;

public class CRUDTests : IAsyncLifetime
{
    private PowerSyncDatabase db = default!;

    public async Task InitializeAsync()
    {
        db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = "crudtest12xas.db" },
            Schema = TestSchema.appSchema,
        });
        await db.Init();
    }

    public async Task DisposeAsync()
    {
        await db.DisconnectAndClear();
        await db.Close();
    }

    [Fact(Skip = "Need to delete db file")]
    public async Task Insert_ShouldRecordCrudEntry()
    {
        string testId = Guid.NewGuid().ToString();

        var initialRows = await db.GetAll<object>("SELECT * FROM ps_crud");
        Assert.Empty(initialRows);

        await db.Execute("INSERT INTO assets(id, description) VALUES(?, ?)", [testId, "test"]);
        var crudEntry = await db.Get<CrudEntryJSON>("SELECT data FROM ps_crud ORDER BY id");

        Assert.Equal(
            JsonConvert.SerializeObject(new
            {
                op = "PUT",
                type = "assets",
                id = testId,
                data = new { description = "test" }
            }),
            crudEntry.Data
        );

        var tx = await db.GetNextCrudTransaction();
        Assert.Equal(1, tx!.TransactionId);

        var expectedCrudEntry = new CrudEntry(1, UpdateType.PUT, "assets", testId, 1, new Dictionary<string, object>
        {
            { "description", "test" }
        });

        Assert.True(tx.Crud.First().Equals(expectedCrudEntry));
    }


}