using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.Client.Sync.Stream;
using PowerSync.Common.Utils;

using System.Dynamic;
using System.IO.Pipelines;
using System.Text;


namespace PowerSync.Common.Tests.Utils.Sync;


public class MockSyncService : EventStream<string>
{
    public void PushLine(StreamingSyncLine line)
    {
        Emit(JsonConvert.SerializeObject(line));
    }

    public IPowerSyncDatabase CreateDatabase()
    {
        var connector = new TestConnector();
        var mockRemote = new MockRemote(connector, this);

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
            builder.SetMinimumLevel(LogLevel.Trace);
        });
        return loggerFactory.CreateLogger("PowerSyncLogger");
    }
}

public class MockDataFactory
{
    //     export function checkpoint(options: { last_op_id: number; buckets?: any[]; streams?: any[] }): StreamingSyncCheckpoint {
    //   return {
    //     checkpoint: {
    //       last_op_id: `${options.last_op_id}`,
    //       buckets: options.buckets ?? [],
    //       write_checkpoint: null,
    //       streams: options.streams ?? []
    //     }
    //   };
    // }

    public static StreamingSyncCheckpoint Checkpoint(long lastOpId, List<BucketChecksum>? buckets = null, List<StreamingSyncCheckpoint>? streams = null)
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
}

public class MockRemote : Remote
{
    private readonly MockSyncService syncService;

    public MockRemote(
        IPowerSyncBackendConnector connector,
        MockSyncService syncService)
        : base(connector)
    {
        this.syncService = syncService;
    }

    public override async Task<Stream> PostStreamRaw(SyncStreamOptions options)
    {
        var pipe = new Pipe();
        var writer = pipe.Writer;
        try
        {
            syncService.RunListenerAsync(async (line) =>
            {
                var bytes = Encoding.UTF8.GetBytes(line);
                await writer.WriteAsync(bytes);
            });
        }
        finally
        {
            await writer.CompleteAsync();
        }

        return pipe.Reader.AsStream();
    }

    public override async Task<T> Get<T>(string path, Dictionary<string, string>? headers = null)
    {

        Console.WriteLine("MockRemote Get: " + path);
        //  return new Response(
        //           JSON.stringify({
        //             data: { write_checkpoint: '1' }
        //           }),
        //           { status: 200 }
        //         );
        throw new Exception("MOOOO");
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
