using Microsoft.Extensions.Time.Testing;

using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.Client.Sync.Stream;
using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Tests.Utils.Sync;

namespace PowerSync.Common.Tests.Client.Sync;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "CheckpointRequestsTests"
/// </summary>
public class CheckpointRequestsTests : IAsyncLifetime
{
    MockSyncService _syncService = null!;
    PowerSyncDatabase _db = null!;

    private static PowerSyncConnectionOptions WithRequests(int? retryDelayMs = null) =>
        new(checkpointMode: new CheckpointMode.Requests(), retryDelayMs: retryDelayMs);

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
        await _db.Connect(new TestConnector(), WithRequests());

        // Every iteration reconciles its checkpoint state with the service before requests are allowed.
        await TestUtils.WaitForAsync(() => _syncService.CheckpointRequests.Count == 1);

        await _db.Execute("INSERT INTO lists (id, name) VALUES (?, ?)", ["id", "local write"]);
        var watched = _db.Watch<NameResult>("SELECT name FROM lists", null, new() { TriggerImmediately = true }).GetAsyncEnumerator();
        await watched.MoveNextAsync();

        Assert.Single(watched.Current);
        Assert.Equal("local write", watched.Current[0].name);

        // The local write should eventually be uploaded, which requests a checkpoint.
        await TestUtils.WaitForAsync(() => _syncService.CheckpointRequests.Count == 2);

        _syncService.PushLine(new StreamingSyncCheckpoint
        {
            Checkpoint = new()
            {
                LastOpId = "1",
                Buckets = [MockDataFactory.Bucket("a", 1, subscriptions: Array.Empty<object>())],
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

    [Fact(Timeout = 15000)]
    public async Task CheckpointRequests_ReportsDownloadErrorWhenRequestingCheckpointFails()
    {
        _syncService.CheckpointRequestsSupported = false;

        // Connect() resolves once connected, which never happens here.
        _ = _db.Connect(new TestConnector(), WithRequests());

        await TestUtils.WaitForAsync(() => _db.CurrentStatus.DataFlowStatus.DownloadError != null);

        Assert.False(_db.CurrentStatus.Connected);
        Assert.Contains("/sync/checkpoint-request", _db.CurrentStatus.DataFlowStatus.DownloadError!.Message);
    }

    /// <summary>
    /// The service is allowed to forget checkpoint requests, so an unapplied one has to be re-posted
    /// until it is. Uses a fake clock to skip the (minimum 10s) retry delay, the same way the JS and
    /// Kotlin equivalents of this test use their frameworks' virtual time.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task CheckpointRequests_RepostsCurrentCheckpointUntilApplied()
    {
        var time = new FakeTimeProvider();
        await using var fake = new FakeClockDatabase(_syncService, time);

        await fake.Db.Connect(new TestConnector(), WithRequests());

        // Wait for the initial post (seed).
        await AdvanceUntil(time, () => _syncService.CheckpointRequests.Count >= 1);

        await fake.Db.Execute("INSERT INTO lists (id, name) VALUES (?, ?)", ["id", "local write"]);
        await AdvanceUntil(time, () => _syncService.CheckpointRequests.Count >= 2);

        var requested = _syncService.CheckpointRequests[^1];

        // Nothing acknowledged it, so the same id keeps being posted.
        for (var i = 3; i <= 6; i++)
        {
            await AdvanceUntil(time, () => _syncService.CheckpointRequests.Count >= i);
            Assert.Equal(requested, _syncService.CheckpointRequests[^1]);
        }

        // Finally, include the checkpoint.
        _syncService.PushLine(new StreamingSyncCheckpoint
        {
            Checkpoint = new()
            {
                LastOpId = "0",
                Buckets = [],
                WriteCheckpoint = _syncService.LastWriteCheckpoint.ToString(),
            }
        });
        _syncService.PushLine(new StreamingSyncCheckpointComplete { CheckpointComplete = new() { LastOpId = "0" } });
        await fake.Db.WaitForFirstSync();

        // Which means we shouldn't keep requesting it.
        var totalRequests = _syncService.CheckpointRequests.Count;
        for (var i = 0; i < 20; i++)
        {
            time.Advance(TimeSpan.FromMinutes(3));
            await Task.Yield();
        }
        await Task.Delay(200);
        Assert.Equal(totalRequests, _syncService.CheckpointRequests.Count);
    }

    /// <summary>
    /// Drives <paramref name="time"/> forward until <paramref name="condition"/> holds, yielding to
    /// the real scheduler in between so the sync loops can make progress.
    /// </summary>
    private static async Task AdvanceUntil(
        FakeTimeProvider time,
        Func<bool> condition,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met before the (real time) timeout");
            }

            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }
    }

    /// <summary>A database on a fake clock, torn down independently of the shared one.</summary>
    private sealed class FakeClockDatabase : IAsyncDisposable
    {
        public PowerSyncDatabase Db { get; }

        public FakeClockDatabase(MockSyncService syncService, FakeTimeProvider time)
        {
            Db = syncService.CreateDatabase(timeProvider: time);
            Db.Init().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            var name = Db.Database.Name;
            await Db.Disconnect();
            await Db.Close();
            DatabaseUtils.CleanDb(name);
        }
    }

    /// <summary>
    /// A checkpoint request needs a seeded download iteration, so wanting one has to cut a pending
    /// retry delay short instead of waiting it out.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task CheckpointRequests_DownloadIsRetriedOnCheckpointRequest()
    {
        await _db.Connect(new TestConnector(), WithRequests(retryDelayMs: 10_000));
        await TestUtils.WaitForAsync(() => _syncService.CheckpointRequests.Count >= 1);

        var iterationsBefore = _syncService.Requests.Count;

        // Destroy the connection by sending a bogus line.
        _syncService.PushLine(new StreamingSyncCheckpoint
        {
            Checkpoint = new() { LastOpId = "invalid line", Buckets = [] }
        });
        await TestUtils.WaitForAsync(() => _db.CurrentStatus.DataFlowStatus.DownloadError != null);

        var start = DateTime.UtcNow;
        await _db.Execute("INSERT INTO lists (id, name) VALUES (uuid(), ?)", ["restart plz"]);

        await TestUtils.WaitForAsync(
            () => _syncService.Requests.Count > iterationsBefore,
            TimeSpan.FromSeconds(8));

        var elapsed = DateTime.UtcNow - start;
        Assert.True(
            elapsed < TimeSpan.FromSeconds(8),
            $"Reconnected after {elapsed.TotalSeconds:F1}s, expected the 10s retry delay to be cut short.");
    }

    [Fact(Timeout = 15000)]
    public async Task CheckpointRequests_CanUseCheckpointMethodFromConnector()
    {
        var didRequestCheckpoint = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connector = new TestCustomCheckpointsConnector((_, requestId) =>
        {
            didRequestCheckpoint.TrySetResult(requestId);
            return Task.FromResult(requestId);
        });

        await _db.Connect(connector, WithRequests());

        Assert.Equal("1", await didRequestCheckpoint.Task);

        // The custom implementation replaces the request to the service.
        Assert.Empty(_syncService.CheckpointRequests);
    }

    /// <summary>
    /// Simulates switching users after the old token expired: the client expects a checkpoint of 100,
    /// which the service wouldn't have for another user yet. Posting the existing id lets the service
    /// recognise that this device + user combination needs higher checkpoint ids.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task CheckpointRequests_ReconcilesCheckpointStateOnTokenExpiry()
    {
        _syncService.LastWriteCheckpoint = 100;

        await _db.Connect(new TestConnector(), WithRequests(retryDelayMs: 200));
        await TestUtils.WaitForAsync(() => _syncService.CheckpointRequests.Count == 1);

        _syncService.LastWriteCheckpoint = 0;
        _syncService.PushLine(new StreamingSyncKeepalive { TokenExpiresIn = 0 });

        await TestUtils.WaitForAsync(
            () => _syncService.CheckpointRequests.Count >= 2,
            TimeSpan.FromSeconds(10));
        Assert.Equal(100, _syncService.LastWriteCheckpoint);
    }

    /// <summary>
    /// Seeding runs alongside line processing rather than blocking it.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task CheckpointRequests_ReadsSyncLinesBeforeCheckpointRequestsAreReady()
    {
        var hasInitialRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeInitialRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncService.BeforeCheckpointRequestResponse = async () =>
        {
            hasInitialRequest.TrySetResult(true);
            await completeInitialRequest.Task;
        };

        _ = _db.Connect(new TestConnector(), WithRequests());
        await hasInitialRequest.Task;

        _syncService.PushLine(new StreamingSyncCheckpoint
        {
            Checkpoint = new() { LastOpId = "0", Buckets = [], WriteCheckpoint = "1" }
        });

        await TestUtils.WaitForAsync(() => _db.CurrentStatus.DataFlowStatus.Downloading);
        completeInitialRequest.TrySetResult(true);
    }

    // A class with a settable property rather than a positional record: Dapper can't pick a
    // constructor when the result set is empty and SQLite reports no column type.
    private class NameResult
    {
        public string name { get; set; } = "";
    }
}

class CheckpointRequestConnector : TestConnector, ICustomCheckpointRequestConnector
{
    public Task<string> PostCheckpointRequest(string clientId, string requestId)
    {
        return Task.FromResult(requestId);
    }
}
