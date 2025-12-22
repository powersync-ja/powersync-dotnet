namespace PowerSync.Common.Tests.Client.Sync;

using Microsoft.Data.Sqlite;

using Newtonsoft.Json;

using PowerSync.Common.Client;
using PowerSync.Common.DB.Crud;
using PowerSync.Common.DB.Schema;
using PowerSync.Common.Tests.Utils;

public class CRUDTests : IAsyncLifetime
{
    private PowerSyncDatabase db = default!;
    private readonly string testId = Guid.NewGuid().ToString();
    private readonly string dbName = "crud-test.db";

    public async Task InitializeAsync()
    {
        db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = dbName },
            Schema = TestSchema.AppSchema,
        });
        await db.Init();
    }

    public async Task DisposeAsync()
    {
        await db.DisconnectAndClear();
        await db.Close();
        DatabaseUtils.CleanDb(dbName);
    }

    [Fact]
    public async Task Insert_RecordCrudEntryTest()
    {
        var initialRows = await db.GetAll<object>("SELECT * FROM ps_crud");
        Assert.Empty(initialRows);

        await db.Execute("INSERT INTO assets(id, description) VALUES(?, ?)", [testId, "test"]);
        var crudEntry = await db.Get<CrudEntryJSON>("SELECT data FROM ps_crud ORDER BY id");

        Assert.Equal(
            JsonConvert.SerializeObject(new
            {
                op = "PUT",
                id = testId,
                type = "assets",
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

    private record CountResult(int count);

    [Fact]
    public async Task InsertOrReplaceTest()
    {
        await db.Execute("INSERT INTO assets(id, description) VALUES(?, ?)", [testId, "test"]);
        await db.Execute("DELETE FROM ps_crud WHERE 1");

        // Replace existing entry
        await db.Execute("INSERT OR REPLACE INTO assets(id, description) VALUES(?, ?)", [testId, "test2"]);

        var crudEntry = await db.Get<CrudEntryJSON>("SELECT data FROM ps_crud ORDER BY id");

        Assert.Equal(
            JsonConvert.SerializeObject(new
            {
                op = "PUT",
                id = testId,
                type = "assets",
                data = new { description = "test2" }
            }),
            crudEntry.Data
        );

        var assetCount = await db.Get<CountResult>("SELECT count(*) as count FROM assets");
        Assert.Equal(1, assetCount.count);

        // Test uniqueness constraint
        var ex = await Assert.ThrowsAsync<SqliteException>(() =>
            db.Execute("INSERT INTO assets(id, description) VALUES(?, ?)", [testId, "test3"])
        );

        Assert.Contains("UNIQUE constraint failed", ex.Message);
    }

    [Fact]
    public async Task UpdateTest()
    {
        await db.Execute("INSERT INTO assets(id, description, make) VALUES(?, ?, ?)", [testId, "test", "test"]);
        await db.Execute("DELETE FROM ps_crud WHERE 1");

        await db.Execute("UPDATE assets SET description = ? WHERE id = ?", ["test2", testId]);

        var crudEntry = await db.Get<CrudEntryJSON>("SELECT data FROM ps_crud ORDER BY id");

        Assert.Equal(
            JsonConvert.SerializeObject(new
            {
                op = "PATCH",
                id = testId,
                type = "assets",
                data = new { description = "test2" }
            }),
            crudEntry.Data
        );

        var tx = await db.GetNextCrudTransaction();
        Assert.Equal(2, tx!.TransactionId);

        var expectedCrudEntry = new CrudEntry(2, UpdateType.PATCH, "assets", testId, 2, new Dictionary<string, object>
        {
            { "description", "test2" }
        });

        Assert.True(tx.Crud.First().Equals(expectedCrudEntry));
    }

    [Fact]
    public async Task DeleteTest()
    {
        await db.Execute("INSERT INTO assets(id, description, make) VALUES(?, ?, ?)", [testId, "test", "test"]);
        await db.Execute("DELETE FROM ps_crud WHERE 1");

        await db.Execute("DELETE FROM assets WHERE id = ?", [testId]);

        var crudEntry = await db.Get<CrudEntryJSON>("SELECT data FROM ps_crud ORDER BY id");

        Assert.Equal(
            JsonConvert.SerializeObject(new
            {
                op = "DELETE",
                id = testId,
                type = "assets",
            }),
            crudEntry.Data
        );

        var tx = await db.GetNextCrudTransaction();
        Assert.Equal(2, tx!.TransactionId);

        var expectedCrudEntry = new CrudEntry(2, UpdateType.DELETE, "assets", testId, 2);
        Assert.Equal(expectedCrudEntry, tx.Crud.First());
    }

    [Fact]
    public async Task InsertOnlyTablesTest()
    {
        var logs = new Table(new Dictionary<string, ColumnType>
        {
            { "level", ColumnType.TEXT },
            { "content", ColumnType.TEXT },
        }, new TableOptions
        {
            InsertOnly = true
        });

        Schema insertOnlySchema = new Schema(new Dictionary<string, Table>
        {
            { "logs", logs },
        });

        var uniqueDbName = $"test-{Guid.NewGuid()}.db";

        var insertOnlyDb = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = uniqueDbName },
            Schema = insertOnlySchema,
        });

        await insertOnlyDb.Init();

        var initialCrudRows = await insertOnlyDb.GetAll<object>("SELECT * FROM ps_crud");
        Assert.Empty(initialCrudRows);

        await insertOnlyDb.Execute("INSERT INTO logs(id, level, content) VALUES(?, ?, ?)", [testId, "INFO", "test log"]);

        var crudEntry = await insertOnlyDb.Get<CrudEntryJSON>("SELECT data FROM ps_crud ORDER BY id");

        Assert.Equal(
            JsonConvert.SerializeObject(new
            {
                op = "PUT",
                type = "logs",
                id = testId,
                data = new { content = "test log", level = "INFO" }
            }),
            crudEntry.Data
        );

        var logRows = await insertOnlyDb.GetAll<object>("SELECT * FROM logs");
        Assert.Empty(logRows);

        var tx = await insertOnlyDb.GetNextCrudTransaction();
        Assert.Equal(1, tx!.TransactionId);

        var expectedCrudEntry = new CrudEntry(1, UpdateType.PUT, "logs", testId, 1, new Dictionary<string, object>
        {
            { "content", "test log" },
            { "level", "INFO" }
        });

        Assert.True(tx.Crud.First().Equals(expectedCrudEntry));
    }

    private record QuantityResult(long quantity);

    [Fact]
    public async Task BigNumbersIntegerTest()
    {
        long bigNumber = 1L << 62;
        await db.Execute("INSERT INTO assets(id, quantity) VALUES(?, ?)", [testId, bigNumber]);

        var result = await db.Get<QuantityResult>("SELECT quantity FROM assets WHERE id = ?", [testId]);
        Assert.Equal(bigNumber, result.quantity);

        var crudEntry = await db.Get<CrudEntryJSON>("SELECT data FROM ps_crud ORDER BY id");

        Assert.Equal(
            JsonConvert.SerializeObject(new
            {
                op = "PUT",
                id = testId,
                type = "assets",
                data = new { quantity = bigNumber }
            }),
            crudEntry.Data
        );

        var tx = await db.GetNextCrudTransaction();
        Assert.Equal(1, tx!.TransactionId);

        var expectedCrudEntry = new CrudEntry(1, UpdateType.PUT, "assets", testId, 1, new Dictionary<string, object>
        {
            { "quantity", bigNumber }
        });

        Assert.True(tx.Crud.First().Equals(expectedCrudEntry));
    }

    [Fact]
    public async Task BigNumbersTextTest()
    {
        long bigNumber = 1L << 62;
        await db.Execute("INSERT INTO assets(id, quantity) VALUES(?, ?)", [testId, bigNumber.ToString()]);

        var result = await db.Get<QuantityResult>("SELECT quantity FROM assets WHERE id = ?", [testId]);
        Assert.Equal(bigNumber, result.quantity);

        var crudEntry = await db.Get<CrudEntryJSON>("SELECT data FROM ps_crud ORDER BY id");

        Assert.Equal(
            JsonConvert.SerializeObject(new
            {
                op = "PUT",
                id = testId,
                type = "assets",
                data = new { quantity = bigNumber.ToString() }
            }),
            crudEntry.Data
        );

        await db.Execute("DELETE FROM ps_crud WHERE 1");

        await db.Execute("UPDATE assets SET description = ?, quantity = CAST(quantity AS INTEGER) + 1 WHERE id = ?", [
            "updated",
        testId
        ]);

        crudEntry = await db.Get<CrudEntryJSON>("SELECT data FROM ps_crud ORDER BY id");

        Assert.Equal(
            JsonConvert.SerializeObject(new
            {
                op = "PATCH",
                id = testId,
                type = "assets",
                data = new { description = "updated", quantity = bigNumber + 1 }
            }),
            crudEntry.Data
        );
    }

    [Fact]
    public async Task TransactionGroupingTest()
    {
        var initialCrudRows = await db.GetAll<object>("SELECT * FROM ps_crud");
        Assert.Empty(initialCrudRows);

        await db.WriteTransaction(async (tx) =>
        {
            await tx.Execute("INSERT INTO assets(id, description) VALUES(?, ?)", [testId, "test1"]);
            await tx.Execute("INSERT INTO assets(id, description) VALUES(?, ?)", ["test2", "test2"]);
        });

        await db.WriteTransaction(async (tx) =>
        {
            await tx.Execute("UPDATE assets SET description = ? WHERE id = ?", ["updated", testId]);
        });

        var tx1 = await db.GetNextCrudTransaction();
        Assert.Equal(1, tx1!.TransactionId);

        var expectedCrudEntries = new[]
        {
            new CrudEntry(1, UpdateType.PUT, "assets", testId, 1, new Dictionary<string, object> { { "description", "test1" } }),
            new CrudEntry(2, UpdateType.PUT, "assets", "test2", 1, new Dictionary<string, object> { { "description", "test2" } })
        };

        Assert.True(tx1.Crud.Select((entry, index) => entry.Equals(expectedCrudEntries[index])).All(result => result));
        await tx1.Complete();

        var tx2 = await db.GetNextCrudTransaction();
        Assert.Equal(2, tx2!.TransactionId);

        var expectedCrudEntry2 = new CrudEntry(3, UpdateType.PATCH, "assets", testId, 2, new Dictionary<string, object>
        {
            { "description", "updated" }
        });

        Assert.True(tx2.Crud.First().Equals(expectedCrudEntry2));
        await tx2.Complete();

        var nextTx = await db.GetNextCrudTransaction();
        Assert.Null(nextTx);
    }
}
