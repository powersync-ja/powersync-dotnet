using System.Dynamic;
using System.IO.Pipelines;
using System.Text;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.Client.Sync.Stream;
using PowerSync.Common.DB.Crud;
using PowerSync.Common.Utils;


namespace PowerSync.Common.Tests.Utils.Sync;


public class MockSyncService : EventStream<string>
{
    private readonly List<StreamingSyncRequest> _requests = new();

    public IReadOnlyList<StreamingSyncRequest> Requests => _requests;

    public void PushLine(StreamingSyncLine line)
    {
        Emit(JsonConvert.SerializeObject(line));
    }

    public void PushLine(string line)
    {
        Emit(line);
    }

    public PowerSyncDatabase CreateDatabase()
    {
        var connector = new TestConnector();
        var mockRemote = new MockRemote(connector, this, _requests);

        return new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = "sync-stream.db" },
            Schema = TestSchemaTodoList.AppSchema,
            RemoteFactory = _ => mockRemote,
            Logger = createLogger()
        });
    }

    private ILogger createLogger()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Error);
        });
        return loggerFactory.CreateLogger("PowerSyncLogger");
    }

    public static async Task<SyncStatus> NextStatus(PowerSyncDatabase db)
    {
        var tcs = new TaskCompletionSource<SyncStatus>();
        CancellationTokenSource? cts = null;

        cts = db.RunListenerAsync(async (update) =>
        {
            if (update.StatusChanged != null)
            {
                tcs.TrySetResult(update.StatusChanged);
                cts?.Cancel();
            }
        });

        return await tcs.Task;
    }
}

public class MockDataFactory
{
    public static StreamingSyncCheckpoint Checkpoint(long lastOpId, List<BucketChecksum>? buckets = null, object[]? streams = null)
    {
        return new StreamingSyncCheckpoint
        {
            Checkpoint = new Checkpoint
            {
                LastOpId = $"{lastOpId}",
                Buckets = buckets?.ToArray() ?? [],
                WriteCheckpoint = null,
                Streams = streams?.ToArray() ?? []
            }
        };
    }

    public static StreamingSyncCheckpointPartiallyComplete CheckpointPartiallyComplete(string lastOpId, int priority)
    {
        return new StreamingSyncCheckpointPartiallyComplete
        {
            PartialCheckpointComplete = new PartialCheckpointComplete
            {
                LastOpId = lastOpId,
                Priority = priority
            }
        };
    }

    public static StreamingSyncCheckpointComplete CheckpointComplete(string lastOpId)
    {
        return new StreamingSyncCheckpointComplete
        {
            CheckpointComplete = new CheckpointComplete
            {
                LastOpId = lastOpId
            }
        };
    }

    public static BucketChecksum Bucket(string name, int count, int priority = 3, object? subscriptions = null)
    {
        return new BucketChecksum
        {
            Bucket = name,
            Count = count,
            Checksum = 0,
            Priority = priority,
            Subscriptions = subscriptions
        };
    }


    public static object Stream(string name, bool isDefault, object[]? errors = null)
    {
        return new
        {
            name = name,
            is_default = isDefault,
            errors = errors ?? []
        };
    }
}

public class MockRemote : Remote
{
    private readonly MockSyncService syncService;
    private readonly List<StreamingSyncRequest> connectedListeners;

    public MockRemote(
        IPowerSyncBackendConnector connector,
        MockSyncService syncService,
        List<StreamingSyncRequest> connectedListeners)
        : base(connector)
    {
        this.syncService = syncService;
        this.connectedListeners = connectedListeners;
    }

    public override async Task<Stream> PostStreamRaw(SyncStreamOptions options)
    {
        connectedListeners.Add(options.Data);

        var pipe = new Pipe();
        var writer = pipe.Writer;

        var x = syncService.RunListenerAsync(async (line) =>
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            await writer.WriteAsync(bytes);
        });

        return pipe.Reader.AsStream();
    }

    public override async Task<T> Get<T>(string path, Dictionary<string, string>? headers = null)
    {
        var response = new StreamingSyncImplementation.ApiResponse(
            new StreamingSyncImplementation.ResponseData("1")
        );

        return (T)(object)response;
    }
}

public class TestConnector : IPowerSyncBackendConnector
{
    public async Task<PowerSyncCredentials?> FetchCredentials()
    {
        return new PowerSyncCredentials(
            endpoint: "https://powersync.example.org",
            token: "test"
        );
    }

    public async Task UploadData(IPowerSyncDatabase database)
    {
        var tx = await database.GetNextCrudTransaction();
        if (tx != null)
        {
            await tx.Complete();
        }
    }
}
