namespace PowerSync.Common.Tests.Client.Sync;

using PowerSync.Common.Client;
using PowerSync.Common.DB.Schema;
using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Tests.Utils.Sync;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "SyncStreamsTests"
/// </summary>
public class SyncStreamsTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {

    }

    public async Task DisposeAsync()
    {

    }

    [Fact]
    public async Task BasicSyncStreamTest()
    {
        Console.WriteLine("Starting BasicSyncStreamTest");
        var syncService = new MockSyncService();
        var db = syncService.CreateDatabase();

        await db.Connect(new TestConnector());
        // syncService.PushLine("{\"type\":\"sync_start\",\"stream\":\"test_stream\"}");
        // syncService.PushLine(MockDataFactory.Checkpoint(lastOpId: 12345, streams: []));
        await Task.Delay(1000);

    }
}
