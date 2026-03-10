namespace PowerSync.Common.Tests.Client;

using System.Diagnostics;

using Microsoft.Data.Sqlite;

using PowerSync.Common.Client;
using PowerSync.Common.Tests.Models;
using PowerSync.Common.Tests.Utils;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "PowerSyncDatabaseTests"
/// </summary>
[Collection("PowerSyncDatabaseTests")]
public class PowerSyncDatabaseTests : IAsyncLifetime
{
    private PowerSyncDatabase db = default!;
    private CancellationTokenSource testCts = default!;
    string dbName = default!;

    public async Task InitializeAsync()
    {
        testCts = new();
        dbName = $"PowerSyncDatabase-{Guid.NewGuid():N}.db";

        db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = dbName },
            Schema = TestSchema.AppSchema,
        });
        await db.Init();
    }

    public async Task DisposeAsync()
    {
        testCts.Cancel();
        testCts.Dispose();

        await db.DisconnectAndClear();
        await db.Close();

        DatabaseUtils.CleanDb(dbName);
    }

    private record IdResult(string id);
    private record CountResult(long count);

    private class AssetResult
    {
        public string id { get; set; } = "";
        public string description { get; set; } = "";
        public string? make { get; set; }
    }

    [Fact]
    public async Task QueryWithoutParams()
    {
        var id = Guid.NewGuid().ToString();
        var description = "some desc";
        var make = "some make";

        await db.Execute(
            "INSERT INTO assets(id, description, make) VALUES(?, ?, ?)",
            [id, description, make]
        );

        var result = await db.GetAll<AssetResult>("SELECT id, description, make FROM assets");

        Assert.Single(result);
        var row = result.First();
        Assert.Equal(id, row.id);
        Assert.Equal(description, row.description);
        Assert.Equal(make, row.make);
    }

    [Fact]
    public async Task QueryWithParams()
    {
        var id = Guid.NewGuid().ToString();
        var description = "some desc";
        var make = "some make";

        await db.Execute(
            "INSERT INTO assets(id, description, make) VALUES(?, ?, ?)",
            [id, description, make]
        );

        var result = await db.GetAll<AssetResult>("SELECT id, description, make FROM assets WHERE id = ?", [id]);

        Assert.Single(result);
        var row = result.First();
        Assert.Equal(id, row.id);
        Assert.Equal(description, row.description);
        Assert.Equal(make, row.make);
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
    public async Task QueriesDoNotBlockCaller()
    {
        var writeTask = db.WriteLock(async ctx =>
        {
            // Simulate slow query
            await Task.Delay(200);
        });

        Assert.False(writeTask.IsCompleted, "Write task with 200ms delay completed synchronously instead of yielding");
        await writeTask;
    }

    [Fact]
    public async Task FailedInsert()
    {
        var exception = await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await db.Execute(
                "INSERT INTO assetsfail (id, description, make) VALUES(?, ?, ?)",
                [Guid.NewGuid().ToString(), "some desc", "some make"]
            );
        });

        Assert.Contains("no such table", exception.Message);
    }

    [Fact]
    public async Task SimpleReadTransaction()
    {
        await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O3"]);

        var result = await db.Database.ReadTransaction(async tx =>
        {
            return await tx.GetAll<IdResult>("SELECT id FROM assets");
        });

        Assert.Single(result);
    }

    [Fact]
    public async Task ManualCommit()
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
    public async Task AutoCommit()
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
    public async Task ManualRollback()
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
    public async Task AutoRollback()
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
    public async Task WriteTransactionWithReturn()
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
    public async Task WriteTransactionNestedQuery()
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
    public async Task ReadLockReadOnly()
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
    public async Task ReadLocksQueueIfExceedNumberOfConnections()
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
    public async Task ReadWhileWriteIsRunning()
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
    public async Task BatchExecute()
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
    public async Task QueueSimultaneousExecutions()
    {
        var order = new List<int>();
        var operationCount = 5;

        await db.WriteLock(async context =>
        {
            for (int i = 0; i < operationCount; i++)
            {
                await context.Execute("SELECT * FROM assets");
                order.Add(i);
            }
        });

        var expectedOrder = Enumerable.Range(0, operationCount).ToList();
        Assert.Equal(expectedOrder, order);
    }

    [Fact(Timeout = 2000)]
    public async Task CallUpdateHookOnChanges()
    {
        var result = new TaskCompletionSource<bool>();

        var listener = db.OnChange(new SQLWatchOptions
        {
            Tables = ["assets"],
            Signal = testCts.Token,
        });

        _ = Task.Run(async () =>
        {
            await foreach (var update in listener)
            {
                result.TrySetResult(true);
                testCts.Cancel();
            }
        }, testCts.Token);

        await db.Execute("INSERT INTO assets (id) VALUES(?)", ["099-onchange"]);

        await result.Task;
    }

    [Fact(Timeout = 2000)]
    public async Task ReflectWriteTransactionUpdatesOnReadConnections()
    {
        var watched = new TaskCompletionSource<bool>();

        var listener = db.Watch<CountResult>("SELECT COUNT(*) as count FROM assets", null, new() { Signal = testCts.Token });
        _ = Task.Run(async () =>
        {
            await foreach (var x in listener)
            {
                if (x.First().count == 1)
                {
                    watched.SetResult(true);
                    testCts.Cancel();
                }
            }
        }, testCts.Token);

        await db.WriteTransaction(async tx =>
        {
            await tx.Execute("INSERT INTO assets (id) VALUES(?)", ["099-watch"]);
            await tx.Commit();
        });

        await watched.Task;
    }

    [Fact(Timeout = 5000)]
    public async Task ReflectWriteLockUpdatesOnReadConnections()
    {
        var numberOfAssets = 10_000;

        var watched = new TaskCompletionSource<bool>();

        var listener = db.Watch<CountResult>(
            "SELECT COUNT(*) as count FROM assets",
            null,
            new() { Signal = testCts.Token, TriggerImmediately = true });
        _ = Task.Run(async () =>
        {
            await foreach (var x in listener)
            {
                if (x.First().count == numberOfAssets)
                {
                    watched.TrySetResult(true);
                    testCts.Cancel();
                }
            }
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
    public async Task Insert1000Records_CompleteWithinTimeLimit()
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
    public async Task TestConcurrentReads()
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
    public async Task GetUploadQueueStats()
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
    public async Task QueryDynamic()
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

    [Fact(Timeout = 2500)]
    public async Task Watch_Dynamic()
    {
        string id = Guid.NewGuid().ToString();
        string description = "dynamic description";
        string make = "dynamic make";

        using var sem = new SemaphoreSlim(0);
        dynamic? dynamicAsset = null;

        var listener = db.Watch<AssetResult>("select id, description, make from assets", null, new() { TriggerImmediately = true });
        _ = Task.Run(async () =>
        {
            await foreach (var assets in listener)
            {
                if (assets.Length > 0)
                {
                    dynamicAsset = assets[0];
                }

                sem.Release();
            }
        });

        await db.Execute(
            "insert into assets (id, description, make) values (?, ?, ?)",
            [id, description, make]
        );
        Assert.True(await sem.WaitAsync(500));

        Assert.NotNull(dynamicAsset);

        Assert.Equal(id, dynamicAsset?.id);
        Assert.Equal(description, dynamicAsset?.description);
        Assert.Equal(make, dynamicAsset?.make);
    }

    [Fact(Timeout = 2000)]
    public async Task Watch_Cancelled()
    {
        int callCount = 0;
        using var sem = new SemaphoreSlim(0);

        var listener = db.Watch<IdResult>("select id from assets", null, new() { Signal = testCts.Token, TriggerImmediately = true });
        _ = Task.Run(async () =>
        {
            await foreach (var result in listener)
            {
                Interlocked.Increment(ref callCount);
                sem.Release();
            }
        });
        Assert.True(await sem.WaitAsync(100));
        Assert.Equal(1, callCount);

        await TestUtils.InsertRandomAsset(db);

        Assert.True(await sem.WaitAsync(100));
        Assert.Equal(2, callCount);

        testCts.Cancel();

        await TestUtils.InsertRandomAsset(db);

        // This is failing
        Assert.False(await sem.WaitAsync(100));
        Assert.Equal(2, callCount);
    }

    [Fact(Timeout = 3000)]
    public async Task Watch_MultipleCancelled()
    {
        int callCount = 0;

        void RunQuery(CancellationTokenSource cts, SemaphoreSlim sem)
        {
            var listener = db.Watch<IdResult>("select id from assets", null, new() { Signal = cts.Token });
            _ = Task.Run(async () =>
            {
                await foreach (var update in listener)
                {
                    Interlocked.Increment(ref callCount);
                    sem.Release();
                }
            });
        }

        using var semAlwaysRunning = new SemaphoreSlim(0);
        using var semCancelled = new SemaphoreSlim(0);
        using var ctsAlwaysRunning = CancellationTokenSource.CreateLinkedTokenSource(testCts.Token);
        using var ctsCancelled = CancellationTokenSource.CreateLinkedTokenSource(testCts.Token);

        RunQuery(ctsAlwaysRunning, semAlwaysRunning);
        RunQuery(ctsCancelled, semCancelled);

        await TestUtils.InsertRandomAsset(db);
        await Task.WhenAll(semAlwaysRunning.WaitAsync(), semCancelled.WaitAsync());
        Assert.Equal(2, callCount);

        // Close one query
        ctsCancelled.Cancel();

        await TestUtils.InsertRandomAsset(db);

        // Ensure nothing received from cancelled result
        Assert.False(await semCancelled.WaitAsync(100));

        await semAlwaysRunning.WaitAsync();
        Assert.Equal(3, callCount);

        // Sanity check
        ctsAlwaysRunning.Cancel();

        await TestUtils.InsertRandomAsset(db);

        Assert.False(await semAlwaysRunning.WaitAsync(100));
        Assert.False(await semCancelled.WaitAsync(100));
        Assert.Equal(3, callCount);
    }

    [Fact(Timeout = 3000)]
    public async Task Watch_SchemaReset()
    {
        var dbId = Guid.NewGuid().ToString();
        var localDbName = $"PowerSyncWatchReset_{dbId}.db";
        var db = new PowerSyncDatabase(new()
        {
            Database = new SQLOpenOptions
            {
                DbFilename = localDbName,
            },
            Schema = TestSchema.MakeOptionalSyncSchema(false)
        });

        try
        {
            using var sem = new SemaphoreSlim(0);
            long lastCount = 0;

            const string QUERY = "SELECT COUNT(*) AS count FROM assets";
            var listener = db.Watch<CountResult>(QUERY, null, new() { Signal = testCts.Token, TriggerImmediately = true });
            _ = Task.Run(async () =>
            {
                await foreach (var result in listener)
                {
                    lastCount = result[0].count;
                    sem.Release();
                }

                // Called on cancellation
                sem.Release();
            });
            Assert.True(await sem.WaitAsync(100));

            var resolved = await db.GetSourceTables(QUERY, null);
            Assert.Single(resolved);
            Assert.Contains("ps_data_local__local_assets", resolved);

            for (int i = 0; i < 3; i++)
            {
                await TestUtils.InsertRandomAsset(db);
                Assert.True(await sem.WaitAsync(100));
                Assert.Equal(i + 1, lastCount);
            }
            Assert.Equal(3, lastCount);

            await db.UpdateSchema(TestSchema.MakeOptionalSyncSchema(true));

            resolved = await db.GetSourceTables(QUERY);
            Assert.Single(resolved);
            Assert.Contains("ps_data__assets", resolved);

            Assert.True(await sem.WaitAsync(100));
            Assert.Equal(0, lastCount);

            await db.Execute("insert into assets select * from inactive_local_assets");
            Assert.True(await sem.WaitAsync(500));
            Assert.Equal(3, lastCount);

            // Sanity check
            testCts.Cancel();
            Assert.True(await sem.WaitAsync(500));

            await db.Execute("delete from assets");
            Assert.False(await sem.WaitAsync(100));
            Assert.Equal(3, lastCount);
        }
        finally
        {
            await db.Close();
            DatabaseUtils.CleanDb(localDbName);
        }
    }

    [Fact]
    public async Task Attributes_ColumnAliasing()
    {
        var localDbName = $"PowerSyncAttributesTest-{Guid.NewGuid():N}.db";
        var db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = localDbName },
            Schema = TestSchemaAttributes.AppSchema,
        });
        try
        {
            await db.DisconnectAndClear();

            var id = Guid.NewGuid().ToString();
            var description = "Test description";
            var completed = false;
            var createdAt = DateTimeOffset.Now;

            await db.Execute(
                "INSERT INTO todos (id, description, completed, created_at) VALUES (?, ?, ?, ?)",
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
        finally
        {
            await db.Close();
            DatabaseUtils.CleanDb(localDbName);
        }
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

    [Fact]
    public async Task Watch_TriggerImmediately_True()
    {
        // Insert some data
        await TestUtils.InsertRandomAssets(db, 3);

        var listener = db.Watch<CountResult>(
            "SELECT COUNT(*) AS count FROM assets",
            null,
            new() { Signal = testCts.Token, TriggerImmediately = true });
        var enumerator = listener.GetAsyncEnumerator();

        var moveNext = enumerator.MoveNextAsync().AsTask();
        var timeout = Task.Delay(500);
        Assert.NotEqual(timeout, await Task.WhenAny(moveNext, timeout));
        Assert.True(await moveNext);

        var current = enumerator.Current;
        Assert.Single(current);
        Assert.Equal(3, current[0].count);
    }

    [Fact]
    public async Task Watch_TriggerImmediately_False()
    {
        var listener = db.Watch<CountResult>(
            "SELECT COUNT(*) AS count FROM assets",
            null,
            new() { TriggerImmediately = false });

        var enumerator = listener.GetAsyncEnumerator();

        var moveNext = enumerator.MoveNextAsync().AsTask();
        var timeout = Task.Delay(200);
        Assert.Equal(timeout, await Task.WhenAny(moveNext, timeout));

        // Trigger the watch to run
        await TestUtils.InsertRandomAssets(db, 3);

        timeout = Task.Delay(500);
        Assert.NotEqual(timeout, await Task.WhenAny(moveNext, timeout));
        Assert.True(await moveNext);

        var current = enumerator.Current;
        Assert.Single(current);
        Assert.Equal(3, current[0].count);
    }

    [Fact(Timeout = 2000)]
    public async Task Watch_CancelsOnTokenCancellation()
    {
        var tcs = new TaskCompletionSource<bool>();

        var listener = db.Watch<CountResult>(
            "SELECT COUNT(*) AS count FROM assets",
            null,
            new() { Signal = testCts.Token });

        // Sem == received result
        // TCS == received cancellation
        _ = Task.Run(async () =>
        {
            await foreach (var _ in listener) { }
            tcs.TrySetResult(true);
        });

        await TestUtils.InsertRandomAssets(db, 3);

        testCts.Cancel();
        Assert.True(await tcs.Task);
    }

    [Fact(Timeout = 5000)]
    public async Task OnChange_ThrottlesBatchesRapidChanges()
    {
        int eventCount = 0;
        using var sem = new SemaphoreSlim(0);
        var tcs = new TaskCompletionSource<bool>();

        var listener = db.OnChange(new SQLWatchOptions
        {
            Tables = ["assets"],
            Signal = testCts.Token,
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in listener)
                {
                    Interlocked.Increment(ref eventCount);
                    sem.Release();
                }
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        for (int i = 0; i < 5; i++)
        {
            await TestUtils.InsertRandomAsset(db);
        }

        // Wait for the throttled event to arrive
        Assert.True(await sem.WaitAsync(200));
        testCts.Cancel();
        Assert.True(await tcs.Task);
        Assert.Equal(1, eventCount);
    }

    [Fact(Timeout = 5000)]
    public async Task Watch_ThrottlesBatchesRapidChanges()
    {
        int eventCount = 0;
        long lastCount = 0;
        using var sem = new SemaphoreSlim(0);
        var tcs = new TaskCompletionSource<bool>();

        var listener = db.Watch<CountResult>(
            "SELECT COUNT(*) AS count FROM assets",
            null,
            new() { Signal = testCts.Token });

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var rows in listener)
                {
                    lastCount = rows.First().count;
                    Interlocked.Increment(ref eventCount);
                    sem.Release();
                }
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        for (int i = 0; i < 5; i++)
        {
            await TestUtils.InsertRandomAsset(db);
        }

        Assert.True(await sem.WaitAsync(200));
        testCts.Cancel();
        Assert.True(await tcs.Task);

        Assert.Equal(5, lastCount);
        Assert.True(eventCount < 5, $"Expected fewer than 5 events but got {eventCount}");
    }

    [Fact(Timeout = 5000)]
    public async Task OnChange_NoThrottleWhenZero()
    {
        int eventCount = 0;
        using var sem = new SemaphoreSlim(0);

        var listener = db.OnChange(new SQLWatchOptions
        {
            Tables = ["assets"],
            Signal = testCts.Token,
            ThrottleMs = 0,
        });

        _ = Task.Run(async () =>
        {
            await foreach (var _ in listener)
            {
                Interlocked.Increment(ref eventCount);
                sem.Release();
            }
        }, testCts.Token);

        for (int i = 0; i < 5; i++)
        {
            await db.Execute("INSERT INTO assets(id, description) VALUES(?, ?)", [Guid.NewGuid().ToString(), "test"]);
            Assert.True(await sem.WaitAsync(500));
        }

        Assert.Equal(5, eventCount);
    }

    [Fact(Timeout = 5000)]
    public async Task OnChange_FirstChangeIsDelayedByThrottle()
    {
        using var sem = new SemaphoreSlim(0);
        var sw = Stopwatch.StartNew();

        var listener = db.OnChange(new SQLWatchOptions
        {
            Tables = ["assets"],
            Signal = testCts.Token,
            ThrottleMs = 500,
        });

        _ = Task.Run(async () =>
        {
            await foreach (var _ in listener)
            {
                sem.Release();
                break;
            }
        }, testCts.Token);

        await db.Execute("INSERT INTO assets(id, description) VALUES(?, ?)", [Guid.NewGuid().ToString(), "test"]);

        Assert.True(await sem.WaitAsync(2000));
        Assert.True(sw.ElapsedMilliseconds >= 400, $"First event took {sw.ElapsedMilliseconds}ms, expected >=400ms (trailing-only throttle)");
    }

    [Fact(Timeout = 5000)]
    public async Task OnChange_ThrottleCancelledCleanly()
    {
        int eventCount = 0;
        var tcs = new TaskCompletionSource<bool>();

        var listener = db.OnChange(new SQLWatchOptions
        {
            Tables = ["assets"],
            Signal = testCts.Token,
            ThrottleMs = 200,
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in listener)
                {
                    Interlocked.Increment(ref eventCount);
                }
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        // Insert to trigger the throttle, then cancel before the window expires
        await db.Execute("INSERT INTO assets(id, description) VALUES(?, ?)", [Guid.NewGuid().ToString(), "test"]);
        await Task.Delay(100);
        testCts.Cancel();

        Assert.True(await tcs.Task);
        // The flush-on-cancel should still deliver the accumulated event
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task Watch_SingleEventForBatchedQuery()
    {
        int eventCount = 0;
        long lastCount = 0;
        using var sem = new SemaphoreSlim(0);
        var tcs = new TaskCompletionSource<bool>();

        var listener = db.Watch<CountResult>(
            "select count(*) as count from assets",
            null,
            new() { Signal = testCts.Token });

        _ = Task.Run(async () =>
        {
            await foreach (var rows in listener)
            {
                lastCount = rows[0].count;
                Interlocked.Increment(ref eventCount);
                sem.Release();
            }
            tcs.TrySetResult(true);
        });

        const int QUERY_COUNT = 1000;
        await TestUtils.InsertRandomAssets(db, QUERY_COUNT);

        Assert.True(await sem.WaitAsync(200));
        testCts.Cancel();
        Assert.True(await tcs.Task);
        Assert.Equal(1, eventCount);
        Assert.Equal(QUERY_COUNT, lastCount);
    }

    [Fact]
    public async Task OnChange_SingleEventsForBatchedQuery()
    {
        int eventCount = 0;
        using var sem = new SemaphoreSlim(0);
        var tcs = new TaskCompletionSource<bool>();

        var listener = db.OnChange(new()
        {
            Tables = ["assets"],
            Signal = testCts.Token,
        });

        _ = Task.Run(async () =>
        {
            await foreach (var _ in listener)
            {
                Interlocked.Increment(ref eventCount);
                sem.Release();
            }
            tcs.TrySetResult(true);
        });

        const int QUERY_COUNT = 1000;
        await TestUtils.InsertRandomAssets(db, QUERY_COUNT);

        Assert.True(await sem.WaitAsync(200));
        testCts.Cancel();
        Assert.True(await tcs.Task);

        Assert.Equal(1, eventCount);
    }
}
