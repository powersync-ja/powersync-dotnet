namespace PowerSync.Common.Tests.Client;

using System.Diagnostics;

using Microsoft.Data.Sqlite;

using Newtonsoft.Json;

using PowerSync.Common.Client;
using PowerSync.Common.DB.Schema;
using PowerSync.Common.Tests.Models;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "PowerSyncDatabaseTests"
/// </summary>
public class PowerSyncDatabaseTests : IAsyncLifetime
{
    private PowerSyncDatabase db = default!;

    public async Task InitializeAsync()
    {
        db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = "powersyncDataBaseTransactions.db" },
            Schema = TestSchema.AppSchema,
        });
        await db.Init();
    }

    public async Task DisposeAsync()
    {
        await db.DisconnectAndClear();
        await db.Close();
    }

    private record IdResult(string id);
    private record CountResult(long count);

    private class AssetResult
    {
        public string id { get; set; }
        public string description { get; set; }
        public string? make { get; set; }
    }

    [Fact]
    public async Task QueryWithoutParamsTest()
    {
        var name = "Test User";
        var age = 30;

        await db.Execute(
            "INSERT INTO assets(id, description, make) VALUES(?, ?, ?)",
            [Guid.NewGuid().ToString(), name, age.ToString()]
        );

        var result = await db.GetAll<AssetResult>("SELECT id, description, make FROM assets");

        Assert.Single(result);
        var row = result.First();
        Assert.Equal(name, row.description);
        Assert.Equal(age.ToString(), row.make);
    }

    [Fact]
    public async Task QueryWithParamsTest()
    {
        var id = Guid.NewGuid().ToString();
        var name = "Test User";
        var age = 30;

        await db.Execute(
            "INSERT INTO assets(id, description, make) VALUES(?, ?, ?)",
            [id, name, age.ToString()]
        );

        var result = await db.GetAll<AssetResult>("SELECT id, description, make FROM assets WHERE id = ?", [id]);

        Assert.Single(result);
        var row = result.First();
        Assert.Equal(id, row.id);
        Assert.Equal(name, row.description);
        Assert.Equal(age.ToString(), row.make);
    }

    [Fact]
    public async Task QueryWithNullParams()
    {
        var id = Guid.NewGuid().ToString();
        var description = "Test description";

        await db.Execute(
            "INSERT INTO assets(id, description, make) VALUES(?, ?, ?)",
            [id, description, null]
        );

        var result = await db.GetAll<AssetResult>("SELECT id, description, make FROM assets");

        Assert.Single(result);
        var row = result.First();
        Assert.Equal(id, row.id);
        Assert.Equal(description, row.description);
        Assert.Null(row.make);
    }

    [Fact]
    public async Task FailedInsertTest()
    {
        var name = "Test User";
        var age = 30;

        var exception = await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await db.Execute(
                "INSERT INTO assetsfail (id, description, make) VALUES(?, ?, ?)",
                [Guid.NewGuid().ToString(), name, age.ToString()]
            );
        });

        Assert.Contains("no such table", exception.Message);
    }

    [Fact]
    public async Task SimpleReadTransactionTest()
    {
        await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O3"]);

        var result = await db.Database.ReadTransaction(async tx =>
        {
            return await tx.GetAll<IdResult>("SELECT id FROM assets");
        });

        Assert.Single(result);
    }

    [Fact]
    public async Task ManualCommitTest()
    {
        await db.WriteTransaction(async tx =>
        {
            await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O4"]);
            await tx.Commit();
        });

        var result = await db.GetAll<IdResult>("SELECT id FROM assets WHERE id = ?", ["O4"]);

        Assert.Single(result);
        Assert.Equal("O4", result.First().id);
    }

    [Fact]
    public async Task AutoCommitTest()
    {
        await db.WriteTransaction(async tx =>
        {
            await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O41"]);
        });

        var result = await db.GetAll<IdResult>("SELECT id FROM assets WHERE id = ?", ["O41"]);

        Assert.Single(result);
        Assert.Equal("O41", result.First().id);
    }

    [Fact]
    public async Task ManualRollbackTest()
    {
        await db.WriteTransaction(async tx =>
        {
            await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O5"]);
            await tx.Rollback();
        });

        var result = await db.GetAll("SELECT * FROM assets");
        Assert.Empty(result);
    }

    [Fact]
    public async Task AutoRollbackTest()
    {
        bool exceptionThrown = false;
        try
        {
            await db.WriteTransaction(async tx =>
            {
                // This should throw an exception
                await tx.Execute("INSERT INTO assets(id) VALUES_SYNTAX_ERROR(?)", ["O5"]);
            });
        }
        catch (Exception ex)
        {
            Assert.Contains("near \"VALUES_SYNTAX_ERROR\": syntax error", ex.Message);
            exceptionThrown = true;
        }

        var result = await db.GetAll("SELECT * FROM assets");
        Assert.Empty(result);
        Assert.True(exceptionThrown);
    }

    [Fact]
    public async Task WriteTransactionWithReturnTest()
    {
        var result = await db.WriteTransaction(async tx =>
        {
            await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O5"]);
            return await tx.GetAll<IdResult>("SELECT id FROM assets");
        });

        Assert.Single(result);
        Assert.Equal("O5", result.First().id);
    }

    [Fact]
    public async Task WriteTransactionNestedQueryTest()
    {
        await db.WriteTransaction(async tx =>
        {
            await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O6"]);

            var txQuery = await tx.GetAll("SELECT * FROM assets");
            Assert.Single(txQuery);

            var dbQuery = await db.GetAll("SELECT * FROM assets");
            Assert.Empty(dbQuery);
        });
    }

    [Fact]
    public async Task ReadLockReadOnlyTest()
    {
        string id = Guid.NewGuid().ToString();
        bool exceptionThrown = false;

        try
        {
            await db.ReadLock<object>(async context =>
            {
                return await context.Execute(
                    "INSERT INTO assets (id) VALUES (?)",
                    [id]
                );
            });

            // If no exception is thrown, fail the test
            throw new Exception("Did not throw");
        }
        catch (Exception ex)
        {
            Assert.Contains("attempt to write a readonly database", ex.Message);
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }

    [Fact]
    public async Task ReadLocksQueueIfExceedNumberOfConnectionsTest()
    {
        string id = Guid.NewGuid().ToString();

        await db.Execute(
            "INSERT INTO assets (id) VALUES (?)",
            [id]
        );

        int numberOfReads = 20;
        var tasks = Enumerable.Range(0, numberOfReads)
            .Select(_ => db.ReadLock(async context =>
            {
                return await context.GetAll<IdResult>("SELECT id FROM assets WHERE id = ?", [id]);
            }))
            .ToArray();

        var lockResults = await Task.WhenAll(tasks);

        var ids = lockResults.Select(r => r.FirstOrDefault()?.id).ToList();

        Assert.All(ids, n => Assert.Equal(id, n));
    }

    [Fact(Timeout = 2000)]
    public async Task ReadWhileWriteIsRunningTest()
    {
        var sem = new TaskCompletionSource<bool>();

        // This wont resolve or free until another connection free's it
        var writeTask = db.WriteLock(async context =>
        {
            await sem.Task; // Wait until read lock signals to proceed
        });

        var readTask = db.ReadLock(async context =>
        {
            // Read logic could execute here while writeLock is still open
            sem.SetResult(true);
            await Task.CompletedTask;
            return 42;
        });

        var result = await readTask;
        await writeTask;

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task BatchExecuteTest()
    {
        var id1 = Guid.NewGuid().ToString();
        var description1 = "Asset 1";
        var make1 = "Make 1";

        var id2 = Guid.NewGuid().ToString();
        var description2 = "Asset 2";
        var make2 = "Make 2";

        var sql = "INSERT INTO assets (id, description, make) VALUES(?, ?, ?)";
        object?[][] parameters = [
            [id1, description1, make1],
            [id2, description2, make2]
        ];

        await db.ExecuteBatch(sql, parameters);

        var result = await db.GetAll<AssetResult>("SELECT id, description, make FROM assets ORDER BY description");

        Assert.Equal(2, result.Length);
        Assert.Equal(id1, result[0].id);
        Assert.Equal(description1, result[0].description);
        Assert.Equal(make1, result[0].make);
        Assert.Equal(id2, result[1].id);
        Assert.Equal(description2, result[1].description);
        Assert.Equal(make2, result[1].make);
    }

    [Fact(Timeout = 2000)]
    public async Task QueueSimultaneousExecutionsTest()
    {
        var order = new List<int>();
        var operationCount = 5;

        await db.WriteLock(async context =>
        {
            var tasks = Enumerable.Range(0, operationCount)
                .Select(async index =>
                {
                    await context.Execute("SELECT * FROM assets");
                    order.Add(index);
                })
                .ToArray();

            await Task.WhenAll(tasks);
        });

        var expectedOrder = Enumerable.Range(0, operationCount).ToList();
        Assert.Equal(expectedOrder, order);
    }

    [Fact(Timeout = 2000)]
    public async Task CallUpdateHookOnChangesTest()
    {
        var cts = new CancellationTokenSource();
        var result = new TaskCompletionSource<bool>();

        db.OnChange(new WatchOnChangeHandler
        {
            OnChange = (x) =>
            {
                result.SetResult(true);
                cts.Cancel();
                return Task.CompletedTask;
            }
        }, new SQLWatchOptions
        {
            Tables = ["assets"],
            Signal = cts.Token
        });
        await db.Execute("INSERT INTO assets (id) VALUES(?)", ["099-onchange"]);

        await result.Task;
    }

    [Fact(Timeout = 2000)]
    public async Task ReflectWriteTransactionUpdatesOnReadConnectionsTest()
    {
        var watched = new TaskCompletionSource<bool>();

        var cts = new CancellationTokenSource();
        await db.Watch("SELECT COUNT(*) as count FROM assets", null, new WatchHandler<CountResult>
        {
            OnResult = (x) =>
            {
                if (x.First().count == 1)
                {
                    watched.SetResult(true);
                    cts.Cancel();
                }
            }
        }, new SQLWatchOptions
        {
            Signal = cts.Token
        });

        await db.WriteTransaction(async tx =>
        {
            await tx.Execute("INSERT INTO assets (id) VALUES(?)", ["099-watch"]);
        });

        await watched.Task;
    }

    [Fact(Timeout = 2000)]
    public async Task ReflectWriteLockUpdatesOnReadConnectionsTest()
    {
        var numberOfAssets = 10_000;

        var watched = new TaskCompletionSource<bool>();

        var cts = new CancellationTokenSource();
        await db.Watch("SELECT COUNT(*) as count FROM assets", null, new WatchHandler<CountResult>
        {
            OnResult = (x) =>
            {
                if (x.First().count == numberOfAssets)
                {
                    watched.SetResult(true);
                    cts.Cancel();
                }
            }
        }, new SQLWatchOptions
        {
            Signal = cts.Token
        });

        await db.WriteLock(async tx =>
        {
            await tx.Execute("BEGIN");
            for (var i = 0; i < numberOfAssets; i++)
            {
                await tx.Execute("INSERT INTO assets (id) VALUES(?)", ["0" + i + "-writelock"]);
            }
            await tx.Execute("COMMIT");
        });

        await watched.Task;
    }

    [Fact(Timeout = 5000)]
    public async Task Insert1000Records_CompleteWithinTimeLimitTest()
    {
        var random = new Random();
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < 1000; ++i)
        {
            int n = random.Next(0, 100000);
            await db.Execute(
                "INSERT INTO assets(id, description) VALUES(?, ?)",
                [(i + 1).ToString(), n]
            );
        }

        await db.Execute("PRAGMA wal_checkpoint(RESTART)");

        stopwatch.Stop();
        var duration = stopwatch.ElapsedMilliseconds;

        Assert.True(duration < 2000, $"Test took too long: {duration}ms");
    }

    [Fact(Timeout = 5000)]
    public async Task TestConcurrentReadsTest()
    {
        await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O6-conccurent-1"]);
        var sem = new TaskCompletionSource<bool>();

        // Start a long-running write transaction
        var transactionTask = Task.Run(async () =>
        {
            await db.WriteTransaction(async tx =>
            {
                await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O6-conccurent-2"]);
                await sem.Task;
                await tx.Commit();
            });
        });

        // Try and read while the write transaction is still open
        var result = await db.GetAll("SELECT * FROM assets");
        Assert.Single(result); // The transaction is not commited yet, we should only read 1 asset

        // Let the transaction complete
        sem.SetResult(true);
        await transactionTask;

        // Read again after the transaction is committed
        var afterTx = await db.GetAll("SELECT * FROM assets");
        Assert.Equal(2, afterTx.Length);
    }

    [Fact(Timeout = 5000)]
    public async Task GetUploadQueueStatsTest()
    {
        for (var i = 0; i < 100; i++)
        {
            await db.Execute("INSERT INTO assets (id) VALUES(?)", ["0" + i + "-writelock"]);
        }

        var stats = await db.GetUploadQueueStats(true);
        Assert.Equal(100, stats.Count);
        Assert.NotNull(stats.Size);
    }

    [Fact]
    public async Task QueryDynamicTest()
    {
        string id = Guid.NewGuid().ToString();
        string description = "new description";
        string make = "some make";
        await db.Execute(
            "insert into assets (id, description, make) values (?, ?, ?)",
            [id, description, make]
        );

        var dynamicAsset = await db.Get("select id, description, make from assets");
        Assert.Equal(id, dynamicAsset.id);
        Assert.Equal(description, dynamicAsset.description);
        Assert.Equal(make, dynamicAsset.make);
    }

    [Fact(Timeout = 2000)]
    public async Task WatchDynamicTest()
    {
        string id = Guid.NewGuid().ToString();
        string description = "new description";
        string make = "some make";

        var watched = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();

        await db.Watch("select id, description, make from assets", null, new WatchHandler<dynamic>
        {
            OnResult = (assets) =>
            {
                // Only care about results after Execute is called
                if (assets.Length == 0) return;

                Assert.Single(assets);
                dynamic dynamicAsset = assets[0];
                Assert.Equal(id, dynamicAsset.id);
                Assert.Equal(description, dynamicAsset.description);
                Assert.Equal(make, dynamicAsset.make);

                watched.SetResult(true);
                cts.Cancel();
            },
            OnError = (ex) => throw ex,
        },
        new SQLWatchOptions
        {
            Signal = cts.Token
        });

        await db.WriteTransaction(async tx =>
        {
            await tx.Execute(
                "insert into assets (id, description, make) values (?, ?, ?)",
                [id, description, make]
            );
        });

        await watched.Task;
    }

    [Fact(Timeout = 2000)]
    public async Task WatchDisposableSubscriptionTest()
    {
        int callCount = 0;
        var semaphore = new SemaphoreSlim(0);

        var query = await db.Watch("select id from assets", null, new()
        {
            OnResult = (results) =>
            {
                callCount++;
                semaphore.Release();
            },
            OnError = (ex) => Assert.Fail(ex.ToString())
        });
        await semaphore.WaitAsync();
        Assert.Equal(1, callCount);

        await db.Execute(
            "insert into assets(id, description, make) values (?, ?, ?)",
            [Guid.NewGuid().ToString(), "some desc", "some make"]
        );
        await semaphore.WaitAsync();
        Assert.Equal(2, callCount);

        query.Dispose();

        await db.Execute(
            "insert into assets(id, description, make) values (?, ?, ?)",
            [Guid.NewGuid().ToString(), "some desc", "some make"]
        );
        bool receivedResult = await semaphore.WaitAsync(100);
        Assert.False(receivedResult, "Received update after disposal");
        Assert.Equal(2, callCount);
    }

    [Fact(Timeout = 2000)]
    public async Task WatchDisposableCustomTokenTest()
    {
        var customTokenSource = new CancellationTokenSource();
        int callCount = 0;
        var sem = new SemaphoreSlim(0);

        using var query = await db.Watch("select id, description, make from assets", null, new()
        {
            OnResult = (results) =>
            {
                callCount++;
                sem.Release();
            },
            OnError = (ex) => Assert.Fail(ex.ToString())
        }, new()
        {
            Signal = customTokenSource.Token
        });
        await sem.WaitAsync();
        Assert.Equal(1, callCount);

        await db.Execute(
            "insert into assets(id, description, make) values (?, ?, ?)",
            [Guid.NewGuid().ToString(), "some desc", "some make"]
        );
        await sem.WaitAsync();
        Assert.Equal(2, callCount);

        customTokenSource.Cancel();

        await db.Execute(
            "insert into assets(id, description, make) values (?, ?, ?)",
            [Guid.NewGuid().ToString(), "some desc", "some make"]
        );
        bool receivedResult = await sem.WaitAsync(100);
        Assert.False(receivedResult, "Received update after disposal");

        Assert.Equal(2, callCount);
    }

    [Fact(Timeout = 3000)]
    public async Task WatchSingleCancelledTest()
    {
        int callCount = 0;

        var watchHandlerFactory = (SemaphoreSlim sem) => new WatchHandler<IdResult>
        {
            OnResult = (result) =>
            {
                Interlocked.Increment(ref callCount);
                sem.Release();
            },
            OnError = (ex) => Assert.Fail(ex.ToString()),
        };

        var semAlwaysRunning = new SemaphoreSlim(0);
        var semCancelled = new SemaphoreSlim(0);
        using var queryAlwaysRunning = await db.Watch("select id from assets", null, watchHandlerFactory(semAlwaysRunning));
        using var queryCancelled = await db.Watch("select id from assets", null, watchHandlerFactory(semCancelled));

        await Task.WhenAll(semAlwaysRunning.WaitAsync(), semCancelled.WaitAsync());
        Assert.Equal(2, callCount);

        await db.Execute(
            "insert into assets(id, description, make) values (?, ?, ?)",
            [Guid.NewGuid().ToString(), "some desc", "some make"]
        );
        await Task.WhenAll(semAlwaysRunning.WaitAsync(), semCancelled.WaitAsync());
        Assert.Equal(4, callCount);

        // Close one query
        queryCancelled.Dispose();

        await db.Execute(
            "insert into assets(id, description, make) values (?, ?, ?)",
            [Guid.NewGuid().ToString(), "some desc", "some make"]
        );

        // Ensure nothing received from cancelled result
        Assert.False(await semCancelled.WaitAsync(100));

        await semAlwaysRunning.WaitAsync();
        Assert.Equal(5, callCount);
    }

    [Fact(Timeout = 3000)]
    public async Task WatchSchemaResetTest()
    {
        var dbId = Guid.NewGuid().ToString();
        var db = new PowerSyncDatabase(new()
        {
            Database = new SQLOpenOptions
            {
                DbFilename = $"powerSyncWatch_{dbId}.db",
            },
            Schema = TestSchema.MakeOptionalSyncSchema(false)
        });

        var sem = new SemaphoreSlim(0);
        long lastCount = -1;

        string querySql = "SELECT COUNT(*) AS count FROM assets";
        var query = await db.Watch(querySql, [], new WatchHandler<CountResult>
        {
            OnResult = (result) =>
            {
                lastCount = result[0].count;
                sem.Release();
            },
            OnError = error => throw error
        });
        Assert.True(await sem.WaitAsync(100));
        Assert.Equal(0, lastCount);

        var resolved = await GetSourceTables(db, querySql);
        Assert.Single(resolved);
        Assert.Contains("ps_data_local__local_assets", resolved);

        for (int i = 0; i < 3; i++)
        {
            await db.Execute(
                "insert into assets(id, description, make) values (?, ?, ?)",
                [Guid.NewGuid().ToString(), "some desc", "some make"]
            );
            Assert.True(await sem.WaitAsync(100));
            Assert.Equal(i + 1, lastCount);
        }
        Assert.Equal(3, lastCount);

        await db.UpdateSchema(TestSchema.MakeOptionalSyncSchema(true));

        resolved = await GetSourceTables(db, querySql);
        Assert.Single(resolved);
        Assert.Contains("ps_data__assets", resolved);

        Assert.True(await sem.WaitAsync(100));
        Assert.Equal(0, lastCount);

        await db.Execute("insert into assets select * from inactive_local_assets");
        Assert.True(await sem.WaitAsync(500));
        Assert.Equal(3, lastCount);

        // Sanity check
        query.Dispose();

        await db.Execute("delete from assets");
        Assert.False(await sem.WaitAsync(100));
        Assert.Equal(3, lastCount);
    }

    private class ExplainedResult
    {
        public int addr = 0;
        public string opcode = "";
        public int p1 = 0;
        public int p2 = 0;
        public int p3 = 0;
        public string p4 = "";
        public int p5 = 0;
    }
    private record TableSelectResult(string tbl_name);
    private async Task<List<string>> GetSourceTables(PowerSyncDatabase db, string sql, object?[]? parameters = null)
    {
        var explained = await db.GetAll<ExplainedResult>(
            $"EXPLAIN {sql}", parameters
        );

        var rootPages = explained
            .Where(row => row.opcode == "OpenRead" && row.p3 == 0)
            .Select(row => row.p2)
            .ToList();

        var tables = await db.GetAll<TableSelectResult>(
            "SELECT DISTINCT tbl_name FROM sqlite_master WHERE rootpage IN (SELECT json_each.value FROM json_each(?))",
            [JsonConvert.SerializeObject(rootPages)]
        );

        return tables.Select(row => row.tbl_name).ToList();
    }

    [Fact]
    public async Task Attributes_ColumnAliasing()
    {
        var db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = "PowerSyncAttributesTest.db" },
            Schema = TestSchemaAttributes.AppSchema,
        });
        await db.DisconnectAndClear();

        var id = Guid.NewGuid().ToString();
        var description = "Test description";
        var completed = false;
        var createdAt = DateTimeOffset.Now;

        await db.Execute(
            "INSERT INTO todos(id, description, completed, created_at, list_id) VALUES(?, ?, ?, ?, uuid())",
            [id, description, completed, createdAt]
        );

        var results = await db.GetAll<Todo>("SELECT * FROM todos");
        Assert.Single(results);
        var row = results.First();
        Assert.Equal(id, row.TodoId);
        Assert.Equal(description, row.Description);
        Assert.Equal(completed, row.Completed);
        Assert.Equal(createdAt, row.CreatedAt);
    }

    [Fact]
    public async Task IndexesCreatedOnTable()
    {
        dynamic[] result = await db.GetAll("SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'ps_data__assets'");
        Assert.Equal(2, result.Length);
        Assert.Equal("sqlite_autoindex_ps_data__assets_1", result[0].name);
        Assert.Equal("ps_data__assets__makemodel", result[1].name);

        result = await db.GetAll("PRAGMA index_info('sqlite_autoindex_ps_data__assets_1')");
        Assert.Single(result); // id
        result = await db.GetAll("PRAGMA index_info('ps_data__assets__makemodel')");
        Assert.Equal(2, result.Length); // make, model
    }
}
