namespace PowerSync.Common.Tests.Client.Sync;

using System.Runtime.CompilerServices;

using PowerSync.Common.Client;
using PowerSync.Common.DB.Schema;
using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Tests.Utils.Sync;

using Common.Client.Sync.Stream;

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

    [Fact]
    public async Task BasicConnectTest()
    {
        Assert.False(db.Connected);
        await db.Connect(new TestConnector());

        Assert.True(db.Connected);
        Assert.Equivalent(new RequestStream { IncludeDefaults = true, Subscriptions = [] }, syncService.Requests[0].Streams);
    }

    [Fact]
    public async Task CanDisableDefaultStreams()
    {
        await db.Connect(new TestConnector(), new PowerSyncConnectionOptions
        {
            IncludeDefaultStreams = false
        });

        Assert.Equivalent(new RequestStream { IncludeDefaults = false, Subscriptions = [] }, syncService.Requests[0].Streams);
    }
}
