namespace PowerSync.Common.Tests.Database;

using PowerSync.Common.Client;

public class PowerSyncDatabaseTransactionTests : IAsyncLifetime
{
    private PowerSyncDatabase db = default!;


    public async Task InitializeAsync()
    {
        db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = "powersyncDataBaseTransactions.db" },
            Schema = TestData.appSchema,
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


    // [Fact]
    // public async Task SimpleReadTransactionTest()
    // {
    //     await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O3"]);

    //     var result = await db.Database.ReadTransaction(async tx =>
    //     {
    //         return await tx.GetAll<IdResult>("SELECT * FROM assets");
    //     });

    //     Assert.Single(result);
    // }

    // [Fact]
    // public async Task ManualCommitTest()
    // {
    //     await db.WriteTransaction(async tx =>
    //     {
    //         await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O4"]);
    //         await tx.Commit();
    //     });

    //     var result = await db.GetAll<IdResult>("SELECT * FROM assets WHERE id = ?", ["O4"]);

    //     Assert.Single(result);
    //     Assert.Equal("O4", result.First().id);
    // }

    // [Fact]
    // public async Task AutoCommitTest()
    // {
    //     await db.WriteTransaction(async tx =>
    //     {
    //         await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O41"]);
    //     });

    //     var result = await db.GetAll<IdResult>("SELECT * FROM assets WHERE id = ?", ["O41"]);

    //     Assert.Single(result);
    //     Assert.Equal("O41", result.First().id);
    // }


    // it('Transaction, manual rollback', async () => {
    //   const {name, age, networth} = generateUserInfo();

    //   await db.writeTransaction(async tx => {
    //     await tx.execute(
    //       'INSERT INTO "users" (id, name, age, networth) VALUES(uuid(), ?, ?, ?)',
    //       [name, age, networth],
    //     );
    //     await tx.rollback();
    //   });

    //   const res = await db.execute('SELECT * FROM users');
    //   expect(res.rows?._array).to.eql([]);
    // });

    // [Fact]
    // public async Task ManualRollbackTest()
    // {
    //     await db.WriteTransaction(async tx =>
    //     {
    //         await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O5"]);
    //         await tx.Rollback();
    //     });

    //     var result = await db.GetAll<object>("SELECT * FROM assets");
    //     Assert.Empty(result);
    // }

    // [Fact]
    // public async Task AutoRollbackTest()
    // {
    //     bool exceptionThrown = false;
    //     try
    //     {
    //         await db.WriteTransaction(async tx =>
    //         {
    //             // This should throw an exception
    //             await tx.Execute("INSERT INTO assets(id) VALUES_SYNTAX_ERROR(?)", ["O5"]);
    //         });
    //     }
    //     catch (Exception ex)
    //     {
    //         Assert.Contains("near \"VALUES_SYNTAX_ERROR\": syntax error", ex.Message);
    //         exceptionThrown = true;
    //     }

    //     var result = await db.GetAll<IdResult>("SELECT * FROM assets");
    //     Assert.Empty(result);
    //     Assert.True(exceptionThrown);
    // }

    // [Fact]
    // public async Task WriteTransactionWithReturnTest()
    // {
    //     var result = await db.WriteTransaction(async tx =>
    //     {
    //         await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O5"]);
    //         return await tx.GetAll<IdResult>("SELECT * FROM assets");
    //     });

    //     Assert.Single(result);
    //     Assert.Equal("O5", result.First().id);
    // }


    // [Fact]
    // public async Task WriteTransactionNestedQueryTest()
    // {
    //     await db.WriteTransaction(async tx =>
    //     {
    //         await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O6"]);

    //         var txQuery = await tx.GetAll<IdResult>("SELECT * FROM assets");
    //         Assert.Single(txQuery);

    //         var dbQuery = await db.GetAll<IdResult>("SELECT * FROM assets");
    //         Assert.Empty(dbQuery);
    //     });
    // }

    // [Fact]
    // public async Task ReadLockShouldBeReadOnlyTest()
    // {
    //     string id = Guid.NewGuid().ToString();
    //     bool exceptionThrown = false;

    //     try
    //     {
    //         await db.ReadLock<object>(async context =>
    //         {
    //             return await context.Execute(
    //                 "INSERT INTO assets (id) VALUES (?)",
    //                 [id]
    //             );
    //         });

    //         // If no exception is thrown, fail the test
    //         throw new Exception("Did not throw");
    //     }
    //     catch (Exception ex)
    //     {
    //         Assert.Contains("attempt to write a readonly database", ex.Message);
    //         exceptionThrown = true;
    //     }

    //     Assert.True(exceptionThrown);
    // }

    // [Fact]
    // public async Task ReadLocksShouldQueueIfExceedNumberOfConnections()
    // {
    //     string id = Guid.NewGuid().ToString();

    //     await db.Execute(
    //         "INSERT INTO assets (id) VALUES (?)",
    //         [id]
    //     );

    //     int numberOfReads = 20;
    //     var tasks = Enumerable.Range(0, numberOfReads)
    //         .Select(_ => db.ReadLock(async context =>
    //         {
    //             return await context.GetAll<AssetResult>("SELECT id FROM assets WHERE id = ?", [id]);
    //         }))
    //         .ToArray();

    //     var lockResults = await Task.WhenAll(tasks);

    //     var ids = lockResults.Select(r => r.FirstOrDefault()?.id).ToList();

    //     Assert.All(ids, n => Assert.Equal(id, n));
    // }

    [Fact(Timeout = 2000)]
    public async Task ShouldBeAbleToReadWhileAWriteIsRunning()
    {
        var tcs = new TaskCompletionSource();

        // This wont resolve or free until another connection free's it
        var writeTask = db.WriteLock(async context =>
        {
            await tcs.Task; // Wait until read lock signals to proceed
        });

        var readTask = db.ReadLock(async context =>
        {
            // Read logic could execute here while writeLock is still open
            tcs.SetResult();
            return 42;
        });

        var result = await readTask;
        await writeTask; // Ensure write task completes

        Assert.Equal(42, result);
    }

    // [Fact(Timeout = 5000)]
    // public async Task TestConcurrentReads()
    // {
    //     await db.Execute("INSERT INTO assets(id) VALUES(?)", ["O6-conccurent-1"]);
    //     var tcs = new TaskCompletionSource<bool>();

    //     // Start a long-running write transaction
    //     var transactionTask = Task.Run(async () =>
    //     {
    //         await db.WriteTransaction(async tx =>
    //         {
    //             await tx.Execute("INSERT INTO assets(id) VALUES(?)", ["O6-conccurent-2"]);
    //             await tcs.Task;
    //             await tx.Commit();
    //         });
    //     });

    //     // Try and read while the write transaction is still open
    //     var result = await db.GetAll<object>("SELECT * FROM assets");
    //     Assert.Single(result); // The transaction is not commited yet, we should only read 1 user

    //     // Let the transaction complete
    //     tcs.SetResult(true);
    //     await transactionTask;

    //     // Read again after the transaction is committed
    //     var afterTx = await db.GetAll<object>("SELECT * FROM assets");
    //     Assert.Equal(2, afterTx.Length);
    // }
}