using Newtonsoft.Json;
using PowerSync.Common.Client;
using System.Data.Common;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;


namespace PowerSync.Common.IntegrationTests;

[Trait("Category", "Integration")]
public class SyncTests : IAsyncLifetime
{
    private record ListResult(string id, string name, string owner_id, string created_at);

    private string userId = Uuid();

    private NodeClient nodeClient = default!;

    private PowerSyncDatabase db = default!;

    public async Task InitializeAsync()
    {
        // Create a logger factory
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger("PowerSyncLogger");

        nodeClient = new NodeClient(userId);
        db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = "powersync-sync-tests.db" },
            Schema = TestSchema.PowerSyncSchema,
            Logger = logger

        });
        await db.Init();
        var connector = new NodeConnector(userId);

        await ClearAllData();

        Console.WriteLine($"Using User ID: {userId}");
        try
        {
            await db.Connect(connector);
            await db.WaitForFirstSync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception during InitializeAsync: {ex}");
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        await ClearAllData();
        await Task.Delay(2000);
        await db.DisconnectAndClear();
        await db.Close();
    }

    [IntegrationFact(Timeout = 3000)]
    public async Task SyncDownCreateOperationTest()
    {
        var watched = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();
        var id = Uuid();

        await db.Watch("select * from lists where id = ?", [id], new WatchHandler<ListResult>
        {
            OnResult = (x) =>
            {
                // Verify that the item was added locally
                if (x.Length == 1)
                {
                    watched.SetResult(true);
                    cts.Cancel();
                }
            }
        }, new SQLWatchOptions
        {
            Signal = cts.Token
        });

        await nodeClient.CreateList(id, name: "Test List magic");
        await watched.Task;
    }

    [IntegrationFact(Timeout = 3000)]
    public async Task SyncDownDeleteOperationTest()
    {
        var watched = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();
        var id = Uuid();

        await nodeClient.CreateList(id, name: "Test List to delete");

        await db.Watch("select * from lists where id = ?", [id], new WatchHandler<ListResult>
        {
            OnResult = (x) =>
            {
                // Verify that the item was added locally
                if (x.Length == 1)
                {
                    watched.SetResult(true);
                    cts.Cancel();
                }
            }
        }, new SQLWatchOptions
        {
            Signal = cts.Token
        });

        await watched.Task;
        await nodeClient.DeleteList(id);

        watched = new TaskCompletionSource<bool>();
        cts = new CancellationTokenSource();

        await db.Watch("select * from lists where id = ?", [id], new WatchHandler<ListResult>
        {
            OnResult = (x) =>
            {
                // Verify that the item was deleted locally
                if (x.Length == 0)
                {
                    watched.SetResult(true);
                    cts.Cancel();
                }
            }
        }, new SQLWatchOptions
        {
            Signal = cts.Token
        });

        await watched.Task;
    }

    [IntegrationFact(Timeout = 5000)]
    public async Task SyncDownLargeCreateOperationTest()
    {
        var watched = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();
        var id = Uuid();
        var listName = Uuid();

        await db.Watch("select * from lists where name = ?", [listName], new WatchHandler<ListResult>
        {
            OnResult = (x) =>
            {
                // Verify that the item was added locally
                if (x.Length == 100)
                {
                    watched.SetResult(true);
                    cts.Cancel();
                }
            }
        }, new SQLWatchOptions
        {
            Signal = cts.Token
        });

        for (int i = 0; i < 100; i++)
        {
            await nodeClient.CreateList(Uuid(), listName);
        }
        await watched.Task;
    }

    [IntegrationFact(Timeout = 5000)]
    public async Task SyncDownCreateOperationAfterLargeUploadTest()
    {
        var localInsertWatch = new TaskCompletionSource<bool>();
        var backendInsertWatch = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();
        var id = Uuid();
        var listName = Uuid();

        await db.Watch("select * from lists where name = ?", [listName], new WatchHandler<ListResult>
        {
            OnResult = (x) =>
            {
                // Verify that the items were added locally
                if (x.Length == 100)
                {
                    localInsertWatch.SetResult(true);
                }
                // Verify that the new item added to backend was synced down
                else if (x.Length == 101)
                {
                    backendInsertWatch.SetResult(true);
                    cts.Cancel();
                }
            }
        }, new SQLWatchOptions
        {
            Signal = cts.Token
        });

        for (int i = 0; i < 100; i++)
        {
            await db.Execute("insert into lists (id, name, owner_id, created_at) values (uuid(), ?, ?, datetime())",
            [listName, userId]);
        }
        await localInsertWatch.Task;

        // let the crud upload finish
        await Task.Delay(2000);

        await nodeClient.CreateList(Uuid(), listName);
        await backendInsertWatch.Task;
    }

    private async Task ClearAllData()
    {
        // Inefficient but simple way to clear all data, avoiding payload limitations
        var results = await db.GetAll<ListResult>("select * from lists");
        foreach (var item in results)
        {
            await nodeClient.DeleteList(item.id);
        }
    }
    static string Uuid()
    {
        return Guid.NewGuid().ToString();
    }
}

[Trait("Category", "Integration")]
public class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS") != "true")
        {
            Skip = "Integration tests are disabled. Set RUN_INTEGRATION_TESTS=true to run.";
        }
    }
}