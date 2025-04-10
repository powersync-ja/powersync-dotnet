namespace PowerSync.Common.Tests.Client;

using System.Diagnostics;
using PowerSync.Common.Client;

public class PowerSyncDatabaseTransactionTests : IAsyncLifetime
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
    private record AssetResult(string id, string description, string? make = null);
    private record CountResult(int count);

    [Fact]
    public async Task SimpleReadTransactionTest()
    {
        await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O3"]);

        var result = await db.Database.ReadTransaction(async tx =>
        {
            return await tx.GetAll<IdResult>("SELECT * FROM assets");
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

        var result = await db.GetAll<IdResult>("SELECT * FROM assets WHERE id = ?", ["O4"]);

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

        var result = await db.GetAll<IdResult>("SELECT * FROM assets WHERE id = ?", ["O41"]);

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

        var result = await db.GetAll<object>("SELECT * FROM assets");
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

        var result = await db.GetAll<IdResult>("SELECT * FROM assets");
        Assert.Empty(result);
        Assert.True(exceptionThrown);
    }

    [Fact]
    public async Task WriteTransactionWithReturnTest()
    {
        var result = await db.WriteTransaction(async tx =>
        {
            await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O5"]);
            return await tx.GetAll<IdResult>("SELECT * FROM assets");
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

            var txQuery = await tx.GetAll<IdResult>("SELECT * FROM assets");
            Assert.Single(txQuery);

            var dbQuery = await db.GetAll<IdResult>("SELECT * FROM assets");
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
                return await context.GetAll<AssetResult>("SELECT id FROM assets WHERE id = ?", [id]);
            }))
            .ToArray();

        var lockResults = await Task.WhenAll(tasks);

        var ids = lockResults.Select(r => r.FirstOrDefault()?.id).ToList();

        Assert.All(ids, n => Assert.Equal(id, n));
    }

    [Fact(Timeout = 2000)]
    public async Task ReadWhileWriteIsRunningTest()
    {
        var tcs = new TaskCompletionSource<bool>();

        // This wont resolve or free until another connection free's it
        var writeTask = db.WriteLock(async context =>
        {
            await tcs.Task; // Wait until read lock signals to proceed
        });

        var readTask = db.ReadLock(async context =>
        {
            // Read logic could execute here while writeLock is still open
            tcs.SetResult(true);
            await Task.CompletedTask;
            return 42;
        });

        var result = await readTask;
        await writeTask;

        Assert.Equal(42, result);
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
                [i + 1, n]
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
        var tcs = new TaskCompletionSource<bool>();

        // Start a long-running write transaction
        var transactionTask = Task.Run(async () =>
        {
            await db.WriteTransaction(async tx =>
            {
                await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O6-conccurent-2"]);
                await tcs.Task;
                await tx.Commit();
            });
        });

        // Try and read while the write transaction is still open
        var result = await db.GetAll<object>("SELECT * FROM assets");
        Assert.Single(result); // The transaction is not commited yet, we should only read 1 asset

        // Let the transaction complete
        tcs.SetResult(true);
        await transactionTask;

        // Read again after the transaction is committed
        var afterTx = await db.GetAll<object>("SELECT * FROM assets");
        Assert.Equal(2, afterTx.Length);
    }
}