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

    public long LastWriteCheckpoint { get; set; } = 0;

    private readonly List<long> _checkpointRequests = [];
    public IReadOnlyList<long> CheckpointRequests => _checkpointRequests;

    public void PushLine(StreamingSyncLine line)
    {
        Emit(JsonConvert.SerializeObject(line));
    }

    public void PushLine(string line)
    {
        Emit(line);
    }

    public PowerSyncDatabase CreateDatabase(string? dbFilename = null)
    {
        dbFilename ??= $"sync-stream-{Guid.NewGuid():N}.db";
        var connector = new TestConnector();
        var mockRemote = new MockRemote(connector, this, _requests);

        return new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = dbFilename },
            Schema = TestSchemaTodoList.AppSchema,
            RemoteFactory = _ => mockRemote,
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

    // TODO This should be able to parse and handle /sync/stream AND /sync/checkpoint_request (or whatever the URL is)
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
        else if (options.Path.Contains("/sync/checkpoint-request"))
        {

        }
        else if (options.Path.Contains("/write-checkpoint2.json"))
        {
        }
    }

    public override Task<T> Get<T>(string path, Dictionary<string, string>? headers = null)
    {
        // Write checkpoint
        if (path.Contains("checkpoint2.json"))
        {
            return Task.FromResult(new StreamingSyncImplementation.ApiResponse(
                new StreamingSyncImplementation.ResponseData("1")
            ));
        }

        throw new InvalidOperationException("Not implemented");
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

public record LogRecord(LogLevel LogLevel, string CategoryName, string Message, Exception? Exception);

public class ListLogger(string categoryName, ConcurrentQueue<LogRecord> drain) : ILogger
{
    private readonly string _categoryName = categoryName;
    private readonly ConcurrentQueue<LogRecord> _drain = drain;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _drain.Enqueue(new(logLevel, _categoryName, formatter(state, exception), exception));
    }

    public IDisposable BeginScope<TState>(TState state) => null;
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
