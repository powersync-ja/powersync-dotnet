using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Tests.Utils.Sync;
using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.Client.Sync.Stream;

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

    [Fact(Timeout = 15000)]
    public async Task CheckpointRequests_WarnsCustomConnectorWithoutRequestsEnabled()
    {
        await _db.Connect(new CheckpointRequestConnector());

        var logs = _syncService.Logs;
        Assert.Single(logs);
        Assert.Contains("implements ICustomCheckpointRequestConnector, but Connect() was called without checkpoint requests enabled.", logs[0].Message);
    }

    [Fact(Timeout = 15000)]
    public async Task CheckpointRequests_RequestsCheckpointsForUpdates()
    {
        await _db.Connect(new CheckpointRequestConnector(), new(checkpointMode: new CheckpointMode.Requests()));

        var logs = _syncService.Logs;
        Assert.Single(logs);
        Assert.Contains("implements ICustomCheckpointRequestConnector, but Connect() was called without checkpoint requests enabled.", logs[0].Message);
    }
}

class CheckpointRequestConnector : TestConnector, ICustomCheckpointRequestConnector
{
    public Task<long> PostCheckpointRequest(string clientId, long requestId)
    {
        return Task.FromResult(requestId);
    }
}
