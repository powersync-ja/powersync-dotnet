using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Tests.Utils.Sync;
using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.Client.Sync.Stream;
using PowerSync.Common.Client.Sync.Bucket;

namespace PowerSync.Common.Tests.Client.Sync;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "CheckpointRequestsTests"
/// </summary>
public class CheckpointRequestsTests : IAsyncLifetime
{
    MockSyncService _syncService = null!;
    PowerSyncDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _syncService = new MockSyncService();
        _db = _syncService.CreateDatabase();
        await _db.Init();
    }

    public async Task DisposeAsync()
    {
        await _db.Disconnect();
        await _db.Close();
        _syncService.Close();
        DatabaseUtils.CleanDb(_db.Database.Name);
    }

    [Fact]
    public async Task CheckpointRequests_WarnsCustomConnectorWithoutRequestsEnabled()
    {
        await _db.Connect(new CheckpointRequestConnector());

        var logs = _syncService.Logs;
        Assert.Single(logs);
        Assert.Contains("implements ICustomCheckpointRequestConnector, but Connect() was called without checkpoint requests enabled.", logs[0].Message);
    }

    [Fact]
    public async Task CheckpointRequests_RequestsCheckpointsForUpdates()
    {
        await _db.Connect(new CheckpointRequestConnector(), new(checkpointMode: new CheckpointMode.Requests()));

        await TestUtils.WaitForAsync(() => _syncService.CheckpointRequests.Count == 1);

        await _db.Execute("INSERT INTO lists (id, name) VALUES (?, ?)", ["id", "local write"]);
        var watched = _db.Watch<NameResult>("SELECT name FROM lists", null, new() { TriggerImmediately = true }).GetAsyncEnumerator();
        await watched.MoveNextAsync();

        Assert.Single(watched.Current);
        Assert.Equal("local write", watched.Current[0].name);

        // The local write should eventually be uploaded.
        await TestUtils.WaitForAsync(() => _syncService.CheckpointRequests.Count == 2);

        _syncService.PushLine(new StreamingSyncCheckpoint
        {
            Checkpoint = new()
            {
                LastOpId = "1",
                Buckets = [new() { Bucket = "a", Count = 1, Checksum = 0, Priority = 3 }],
                WriteCheckpoint = _syncService.LastWriteCheckpoint.ToString(),
            }
        });
        _syncService.PushLine(new StreamingSyncDataJSON
        {
            Data = new()
            {
                Bucket = "a",
                Data = [
                    new OplogEntryJSON
                    {
                        Checksum = 0,
                        OpId = "1",
                        ObjectId = "id",
                        ObjectType = "lists",
                        Op = "REMOVE",
                    }
                ]
            }
        });
        _syncService.PushLine(new StreamingSyncCheckpointComplete { CheckpointComplete = new() { LastOpId = "1" } });

        await watched.MoveNextAsync();
        Assert.Empty(watched.Current);
    }
    private record NameResult(string name);
}

class CheckpointRequestConnector : TestConnector, ICustomCheckpointRequestConnector
{
    public Task<long> PostCheckpointRequest(string clientId, long requestId)
    {
        return Task.FromResult(requestId);
    }
}
