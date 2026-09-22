using System.Collections.Concurrent;
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
    private readonly List<StreamingSyncRequest> _requests = [];
    public IReadOnlyList<StreamingSyncRequest> Requests => _requests;

    private readonly ListLoggerProvider _listLoggerProvider = new();
    public IReadOnlyList<LogRecord> Logs => _listLoggerProvider.Logs;

    private readonly object checkpointGate = new();
    private readonly List<long> checkpointRequests = [];
    private long lastWriteCheckpoint;

    /// <summary>
    /// The highest checkpoint request id this service has handed out. Settable so tests can simulate a
    /// client whose local counter has drifted from the service's.
    /// </summary>
    public long LastWriteCheckpoint
    {
        get { lock (checkpointGate) { return lastWriteCheckpoint; } }
        set { lock (checkpointGate) { lastWriteCheckpoint = value; } }
    }

    /// <summary>Every checkpoint request id received on `/sync/checkpoint-request`, in order.</summary>
    public IReadOnlyList<long> CheckpointRequests
    {
        get { lock (checkpointGate) { return [.. checkpointRequests]; } }
    }

    /// <summary>Set to false to emulate a service too old to know `/sync/checkpoint-request`.</summary>
    public bool CheckpointRequestsSupported { get; set; } = true;

    /// <summary>Runs after a checkpoint request is recorded, but before it is answered.</summary>
    public Func<Task> BeforeCheckpointRequestResponse { get; set; } = () => Task.CompletedTask;

    /// <summary>
    /// Answers a checkpoint request the way the service does: the effective id is the higher of the
    /// requested id and the one the service already knows about.
    /// </summary>
    internal async Task<CheckpointRequestResponse> HandleCheckpointRequest(CheckpointRequestPayload request)
    {
        if (!CheckpointRequestsSupported)
        {
            throw new HttpRequestException(
                "Received NotFound - Not Found when getting from /sync/checkpoint-request: ");
        }

        long resolved;
        lock (checkpointGate)
        {
            checkpointRequests.Add(request.CheckpointRequestId);
            resolved = Math.Max(lastWriteCheckpoint, request.CheckpointRequestId);
            lastWriteCheckpoint = resolved;
        }

        await BeforeCheckpointRequestResponse();

        return new CheckpointRequestResponse
        {
            Data = new CheckpointRequestResponseData { CheckpointRequestId = resolved }
        };
    }

    public void PushLine(StreamingSyncLine line)
    {
        Emit(JsonConvert.SerializeObject(line));
    }

    public void PushLine(string line)
    {
        Emit(line);
    }

    public PowerSyncDatabase CreateDatabase(string? dbFilename = null, TimeProvider? timeProvider = null)
    {
        dbFilename ??= $"sync-stream-{Guid.NewGuid():N}.db";
        var connector = new TestConnector();
        var mockRemote = new MockRemote(connector, this, _requests);

        return new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = dbFilename },
            Schema = TestSchemaTodoList.AppSchema,
            RemoteFactory = _ => mockRemote,
            TimeProvider = timeProvider,
            Logger = CreateLogger()
        });
    }

    private ILogger CreateLogger()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddProvider(_listLoggerProvider);
            builder.SetMinimumLevel(LogLevel.Warning);
        });
        return loggerFactory.CreateLogger("PowerSyncLogger");
    }

    public static async Task<SyncStatus> NextStatus(PowerSyncDatabase db)
    {
        var tcs = new TaskCompletionSource<SyncStatus>();
        var cts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            await foreach (var update in db.Events.OnStatusChanged.ListenAsync(cts.Token))
            {
                tcs.TrySetResult(update.Status);
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

    public override Task<Stream> PostStreamRaw(SyncStreamOptions options)
    {
        if (options.Path.EndsWith("/sync/stream"))
        {
            connectedListeners.Add(options.Data);

            var pipe = new Pipe();
            var writer = pipe.Writer;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken);
            var listener = syncService.ListenAsync(cts.Token);
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var line in listener)
                    {
                        var bytes = Encoding.UTF8.GetBytes(line + "\n");
                        await writer.WriteAsync(bytes);
                    }
                }
                finally
                {
                    await writer.CompleteAsync();
                    cts.Cancel();
                    cts.Dispose();
                }
            });

            return Task.FromResult(pipe.Reader.AsStream());
        }

        throw new InvalidOperationException($"MockRemote received an unexpected stream request: {options.Path}");
    }

    public override async Task<T> FetchJson<T>(string path, HttpMethod? method = null, object? data = null, Dictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        if (path.Contains("/sync/checkpoint-request"))
        {
            var response = await syncService.HandleCheckpointRequest((CheckpointRequestPayload)data!);
            return (T)(object)response;
        }

        if (path.Contains("write-checkpoint2.json"))
        {
            return (T)(object)new StreamingSyncImplementation.LegacyWriteCheckpointApiResponse(
                new StreamingSyncImplementation.LegacyWriteCheckpointResponseData(1)
            );
        }

        throw new InvalidOperationException($"MockRemote received an unexpected request: {path}");
    }
}

public class TestConnector : IPowerSyncBackendConnector
{
    public Task<PowerSyncCredentials?> FetchCredentials()
    {
        return Task.FromResult<PowerSyncCredentials?>(new PowerSyncCredentials(
            endpoint: "https://powersync.example.org",
            token: "test"
        ));
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

public class TestCustomCheckpointsConnector(Func<string, long, CancellationToken, Task<long>> postCheckpointRequest) : TestConnector, ICustomCheckpointRequestConnector
{
    private readonly Func<string, long, CancellationToken, Task<long>> _postCheckpointRequest = postCheckpointRequest;

    public Task<long> PostCheckpointRequest(string clientId, long requestId, CancellationToken ct)
        => _postCheckpointRequest(clientId, requestId, ct);
}

public record LogRecord(LogLevel LogLevel, string CategoryName, string Message, Exception? Exception);

public class ListLogger(string categoryName, ConcurrentQueue<LogRecord> drain) : ILogger
{
    private readonly string _categoryName = categoryName;
    private readonly ConcurrentQueue<LogRecord> _drain = drain;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _drain.Enqueue(new(logLevel, _categoryName, formatter(state, exception), exception));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
}

public class ListLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogRecord> _logs = new();
    public IReadOnlyList<LogRecord> Logs => [.. _logs];

    public ILogger CreateLogger(string categoryName)
    {
        return new ListLogger(categoryName, _logs);
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
