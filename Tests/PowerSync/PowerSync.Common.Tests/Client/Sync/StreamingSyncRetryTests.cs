namespace PowerSync.Common.Tests.Client.Sync;

using System.Collections.Concurrent;

using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.Client.Sync.Stream;
using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Tests.Utils.Sync;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "StreamingSyncRetryTests"
/// </summary>
public class StreamingSyncRetryTests
{
    [Fact(Timeout = 15000)]
    public async Task RetryLoop_AppliesDelayBetweenFailedAttempts()
    {
        const int retryDelayMs = 200;
        const double tolerance = 0.75;

        var attemptTimes = new ConcurrentQueue<DateTime>();
        var attemptSignal = new SemaphoreSlim(0);

        var dbFilename = $"sync-retry-{Guid.NewGuid():N}.db";
        var throwing = new ThrowingRemote(new TestConnector(), attemptTimes, attemptSignal);

        var db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = dbFilename },
            Schema = TestSchemaTodoList.AppSchema,
            RemoteFactory = _ => throwing
        });

        try
        {
            await db.Init();

            // Fire-and-forget: Connect() awaits Connected=true, which never fires
            // because every iteration throws. The retry loop runs in the background.
            _ = db.Connect(
                new TestConnector(),
                new PowerSyncConnectionOptions { RetryDelayMs = retryDelayMs }
            );

            for (int i = 0; i < 4; i++)
            {
                Assert.True(
                    await attemptSignal.WaitAsync(TimeSpan.FromSeconds(5)),
                    $"Did not observe attempt #{i + 1} within timeout — retry loop is not running"
                );
            }

            var timestamps = attemptTimes.ToArray();
            Assert.True(timestamps.Length >= 4);

            for (int i = 1; i < timestamps.Length; i++)
            {
                var deltaMs = (timestamps[i] - timestamps[i - 1]).TotalMilliseconds;
                Assert.True(
                    deltaMs >= retryDelayMs * tolerance,
                    $"Retry gap #{i} was {deltaMs:F0}ms, expected >= {retryDelayMs * tolerance:F0}ms (RetryDelayMs={retryDelayMs})"
                );
            }
        }
        finally
        {
            await db.Disconnect();
            await db.Close();
            DatabaseUtils.CleanDb(dbFilename);
        }
    }
}

internal sealed class ThrowingRemote : Remote
{
    private readonly ConcurrentQueue<DateTime> timestamps;
    private readonly SemaphoreSlim signal;

    public ThrowingRemote(
        IPowerSyncBackendConnector connector,
        ConcurrentQueue<DateTime> timestamps,
        SemaphoreSlim signal
    ) : base(connector)
    {
        this.timestamps = timestamps;
        this.signal = signal;
    }

    public override Task<System.IO.Stream> PostStreamRaw(SyncStreamOptions options)
    {
        timestamps.Enqueue(DateTime.UtcNow);
        signal.Release();
        throw new HttpRequestException(
            "HTTP InternalServerError: simulated [PSYNC_S2305] from ThrowingRemote"
        );
    }

    public override Task<T> Get<T>(string path, Dictionary<string, string>? headers = null)
    {
        var response = new StreamingSyncImplementation.ApiResponse(
            new StreamingSyncImplementation.ResponseData("1")
        );
        return Task.FromResult((T)(object)response);
    }
}
