namespace PowerSync.Common.Tests.Client.Sync;

using System.Runtime.CompilerServices;

using PowerSync.Common.Client;
using PowerSync.Common.DB.Schema;
using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Tests.Utils.Sync;

using Common.Client.Sync.Stream;

using Newtonsoft.Json;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "SyncStreamsTests"
/// </summary>
public class SyncStreamsTests : IAsyncLifetime
{

    MockSyncService syncService = null!;
    PowerSyncDatabase db = null!;

    public async Task InitializeAsync()
    {
        syncService = new MockSyncService();
        db = syncService.CreateDatabase();
    }

    public async Task DisposeAsync()
    {
        syncService.Close();
        await db.DisconnectAndClear();
        await db.Close();
    }

    // [Fact]
    // public async Task BasicConnectTest()
    // {
    //     Assert.False(db.Connected);
    //     await db.Connect(new TestConnector());

    //     Assert.True(db.Connected);
    //     Equivalent(new RequestStream { IncludeDefaults = true, Subscriptions = [] }, syncService.Requests[0].Streams);
    // }

    // [Fact]
    // public async Task CanDisableDefaultStreams()
    // {
    //     await db.Connect(new TestConnector(), new PowerSyncConnectionOptions
    //     {
    //         IncludeDefaultStreams = false
    //     });

    //     Equivalent(new RequestStream { IncludeDefaults = false, Subscriptions = [] }, syncService.Requests[0].Streams);
    // }

    [Fact]
    public async Task BasicSubscribeTest()
    {
        var a = await db.SyncStream("a").Subscribe();
        // await db.SyncStream("a").UnsubscribeAll();

        await db.Connect(new TestConnector(), new PowerSyncConnectionOptions());
        Console.WriteLine("After connect" + JsonConvert.SerializeObject(syncService.Requests[0]));
        Console.WriteLine("After connect" + JsonConvert.SerializeObject(syncService.Requests[0].Streams));
        Equivalent(new RequestStream { IncludeDefaults = true, Subscriptions = [] }, syncService.Requests[0].Streams);
        Console.WriteLine("Before unsubscribe" + syncService.Requests.Count);
        // a.Unsubscribe();
    }

    private void Equivalent(object? expected, object? actual, [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            Assert.Equivalent(expected, actual, strict: true);
        }
        catch (Exception ex)
        {
            throw new Exception($"Equivalence assertion failed at line {lineNumber}: {ex.Message}", ex);
        }
    }

    // [Fact]
    // public async Task UnsubscribeAllTest()
    // {
    //     var a = await db.SyncStream("a").Subscribe();
    //     await db.SyncStream("a").UnsubscribeAll();

    //     await db.Connect(new TestConnector(), new PowerSyncConnectionOptions());
    //     Assert.Equivalent(new RequestStream { IncludeDefaults = true, Subscriptions = [] }, syncService.Requests[0].Streams);
    //     a.Unsubscribe();
    // }

    //   mockSyncServiceTest('unsubscribeAll', async ({ syncService }) => {
    //     const database = await syncService.createDatabase();
    //     const a = await database.syncStream('a').subscribe();
    //     database.syncStream('a').unsubscribeAll();

    //     await database.connect(new TestConnector(), defaultOptions);
    //     expect(syncService.connectedListeners[0]).toMatchObject({
    //       streams: {
    //         include_defaults: true,
    //         subscriptions: []
    //       }
    //     });
    //     a.unsubscribe();
    //   });
}
