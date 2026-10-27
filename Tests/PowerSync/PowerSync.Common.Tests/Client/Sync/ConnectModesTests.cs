using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.Client.Sync.Stream;
using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Tests.Utils.Sync;

namespace PowerSync.Common.Tests.Client.Sync;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "ConnectModesTests"
/// </summary>
public class ConnectModesTests : IAsyncLifetime
{
    const string Endpoint = "https://powersynctest.example.com";

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
    public async Task ConnectDownloadOnly()
    {
        await _db.Connect(Endpoint, new TokenAuthenticator(), null);
        await TestUtils.WaitForAsync(() => _db.CurrentStatus.Connected);

        // Create a local mutation, which is never uploaded
        await _db.Execute("INSERT INTO lists (id, name) VALUES (uuid(), ?)", ["local mutation"]);

        // A new checkpoint can't be applied in this state, and should report an error.
        _syncService.PushLine(MockDataFactory.Checkpoint(0));
        _syncService.PushLine(MockDataFactory.CheckpointComplete("0"));
        await TestUtils.WaitForAsync(() => _db.CurrentStatus.DataFlowStatus.DownloadError != null);
        Assert.Contains("blocked on local mutations, but no uploader is set", _db.CurrentStatus.DataFlowStatus.DownloadError!.Message);

        // Upgrade to a full connection, which should upload
        var uploader = new RecordingUploader();
        await _db.Connect(Endpoint, new TokenAuthenticator(), uploader);
        await uploader.DidUpload.Task;
    }

    [Fact(Timeout = 15000)]
    public async Task ConnectUploadOnly()
    {
        // Verify that uploads are retried in upload-only mode too.
        var uploader = new RecordingUploader { FailFirstUpload = true };
        await _db.Connect(uploader, new PowerSyncConnectionOptions(retryDelayMs: 10));

        await _db.Execute("INSERT INTO lists (id, name) VALUES (uuid(), ?)", ["local mutation"]);
        await uploader.DidUpload.Task;

        // Connecting in upload-only mode should make no SDK-initiated requests to the service.
        Assert.Empty(_syncService.Requests);
        Assert.Empty(_syncService.CheckpointRequests);

        // Reconnect in download-only mode. This should request a checkpoint because the upload-only
        // mode can't.
        var authenticator = new HeldCheckpointRequestAuthenticator();
        await _db.Connect(Endpoint, authenticator, null, new PowerSyncConnectionOptions(checkpointMode: new CheckpointMode.Requests()));

        Assert.Equal(["local mutation"], await ListNames());

        var requestId = await authenticator.TargetCheckpointRequest.Task;
        _syncService.PushLine(new StreamingSyncCheckpoint
        {
            Checkpoint = new()
            {
                LastOpId = 1,
                Buckets = [MockDataFactory.Bucket("a", 1, subscriptions: Array.Empty<object>())],
                WriteCheckpoint = requestId,
            }
        });
        _syncService.PushLine(new StreamingSyncDataJSON
        {
            Data = new SyncDataBucketJSON
            {
                Bucket = "a",
                Data = [
                    new OplogEntryJSON
                    {
                        Checksum = 0,
                        OpId = 1,
                        ObjectId = "test1",
                        ObjectType = "lists",
                        Op = "PUT",
                        Data = "{\"name\": \"from server\"}",
                    }
                ]
            }
        });
        _syncService.PushLine(MockDataFactory.CheckpointComplete("1"));

        // The checkpoint can't be applied before the local target is updated.
        await Task.Delay(500);
        Assert.Equal(["local mutation"], await ListNames());

        authenticator.ReleaseCheckpointRequest.SetResult();
        await TestUtils.WaitForAsync(async () => (await ListNames()).SequenceEqual(["from server"]));
    }

    private async Task<string[]> ListNames()
    {
        var rows = await _db.GetAll<NameResult>("SELECT name FROM lists ORDER BY name");
        return [.. rows.Select(r => r.name)];
    }

    private class NameResult
    {
        public string name { get; set; } = "";
    }

    private class TokenAuthenticator : IAuthenticator
    {
        public Task<string> ResolveCredentials() => Task.FromResult("test token");
    }

    private class RecordingUploader : IMutationUploader
    {
        public bool FailFirstUpload { get; init; }
        public TaskCompletionSource DidUpload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool _isFirstUpload = true;

        public async Task UploadMutations(IPowerSyncDatabase database)
        {
            var tx = await database.GetNextCrudTransaction();
            if (tx == null)
            {
                return;
            }

            if (FailFirstUpload && _isFirstUpload)
            {
                _isFirstUpload = false;
                throw new Exception("expected upload error");
            }

            await tx.Complete();
            DidUpload.TrySetResult();
        }
    }

    private class HeldCheckpointRequestAuthenticator : ICustomCheckpointRequestConnector
    {
        public TaskCompletionSource<long> TargetCheckpointRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCheckpointRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool _isSeedRequest = true;

        public Task<string> ResolveCredentials() => Task.FromResult("test token");

        public async Task<long> PostCheckpointRequest(string clientId, long requestId, CancellationToken token)
        {
            if (_isSeedRequest)
            {
                _isSeedRequest = false;
            }
            else
            {
                TargetCheckpointRequest.TrySetResult(requestId);
                await ReleaseCheckpointRequest.Task.WaitAsync(token);
            }
            return requestId;
        }
    }
}
