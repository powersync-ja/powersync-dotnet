namespace PowerSync.Common.Tests.Client.Sync;

using System.Collections.Concurrent;
using System.Text;

using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.Client.Sync.Stream;
using PowerSync.Common.DB.Crud;

/// <summary>
/// Drives a single sync iteration against a scripted core extension so the
/// client's control flow can be asserted without a real powersync_control.
///
/// dotnet test -v n --framework net8.0 --filter "SyncIterationControlFlowTests"
/// </summary>
[Collection("SyncIterationControlFlowTests")]
public class SyncIterationControlFlowTests
{
    private const string EstablishOnly = @"[{""EstablishSyncStream"":{""request"":{}}}]";
    private const string NoInstructions = "[]";
    private const string CloseWithoutHidingDisconnect = @"[{""CloseSyncStream"":{""hide_disconnect"":false}}]";

    /// <summary>
    /// A read failure while the stream is open must reach the outer retry loop,
    /// and the core must be told the connection ended before the iteration stops.
    /// Surfaces when the socket dies mid-sync: Wi-Fi drop, cell handover, or an idle proxy timeout.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task MidStreamReadFailureIsSurfacedToRetryLoop()
    {
        var sentinel = $"read-failed-{Guid.NewGuid():N}";
        var adapter = new ScriptedAdapter((op, _) => op switch
        {
            PowerSyncControlCommand.START => EstablishOnly,
            _ => NoInstructions
        });

        // One line, then the socket dies.
        var harness = Harness.Create(adapter, _ => Task.FromResult<Stream>(
            new FailAfterPrefixStream("{}\n", new IOException(sentinel))));

        var thrown = await Assert.ThrowsAsync<IOException>(() => harness.RunIteration());

        Assert.Equal(sentinel, thrown.Message);
        Assert.Equal(
            new[]
            {
                PowerSyncControlCommand.START,
                PowerSyncControlCommand.CONNECTION_STATE,   // established
                PowerSyncControlCommand.PROCESS_TEXT_LINE,
                PowerSyncControlCommand.CONNECTION_STATE,   // end
                PowerSyncControlCommand.STOP
            },
            adapter.Ops);
    }

    /// <summary>
    /// A failure opening the stream must also reach the retry loop, and the
    /// iteration must still stop the core's iteration on the way out.
    /// Surfaces when the connect POST fails: offline, DNS failure, or a 401 from an expired token.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task StreamOpenFailureIsSurfacedToRetryLoop()
    {
        var sentinel = $"open-failed-{Guid.NewGuid():N}";
        var adapter = new ScriptedAdapter((op, _) => op switch
        {
            PowerSyncControlCommand.START => EstablishOnly,
            _ => NoInstructions
        });

        var harness = Harness.Create(adapter,
            _ => Task.FromException<Stream>(new HttpRequestException(sentinel)));

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => harness.RunIteration());

        Assert.Equal(sentinel, thrown.Message);
        // No "established"/"end" - the stream never opened.
        Assert.Equal(new[] { PowerSyncControlCommand.START, PowerSyncControlCommand.STOP }, adapter.Ops);
    }

    /// <summary>
    /// When START asks for credentials, the refreshed token must still be
    /// reported back to the core.
    /// Surfaces when the cached token is stale at connect time, e.g. resume from background.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task TokenRefreshedDuringStartIsForwardedToCore()
    {
        var refreshed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new ScriptedAdapter((op, _) =>
        {
            switch (op)
            {
                case PowerSyncControlCommand.START:
                    // The core wants a fresh token before it will accept sync lines.
                    return @"[{""EstablishSyncStream"":{""request"":{}}},{""FetchCredentials"":{""did_expire"":false}}]";
                case PowerSyncControlCommand.NOTIFY_TOKEN_REFRESHED:
                    refreshed.TrySetResult(true);
                    return NoInstructions;
                default:
                    return NoInstructions;
            }
        });

        // Stream stays open so the control loop keeps consuming.
        var harness = Harness.Create(adapter, _ => Task.FromResult<Stream>(new HangingStream("")));

        var iteration = harness.RunIteration();
        var forwarded = await Task.WhenAny(refreshed.Task, Task.Delay(3000)) == refreshed.Task;

        harness.Cancel();
        try { await iteration; } catch { /* teardown */ }

        Assert.True(forwarded,
            "Expected 'refreshed_token' to be forwarded to the core after the credential refresh " +
            $"requested by START, but only saw: {string.Join(", ", adapter.Ops)}");
    }

    /// <summary>
    /// Tearing down the stream reader must not leave the in-flight
    /// ReadLineAsync task's exception unobserved - that is exactly the crash
    /// reporting noise this control flow rework is meant to remove.
    /// Surfaces on every teardown with a read in flight: any Disconnect, or a core-initiated close.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task AbandonedReadDoesNotLeakUnobservedException()
    {
        var sentinel = $"abandoned-read-{Guid.NewGuid():N}";
        var unobserved = new ConcurrentBag<string>();
        void Handler(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            foreach (var ex in e.Exception.Flatten().InnerExceptions)
            {
                if (ex.Message.Contains(sentinel))
                {
                    unobserved.Add(ex.Message);
                    e.SetObserved();
                }
            }
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            var adapter = new ScriptedAdapter((op, _) => op switch
            {
                PowerSyncControlCommand.START => EstablishOnly,
                // The core closes the stream on the first line, leaving the next
                // ReadLineAsync in flight.
                PowerSyncControlCommand.PROCESS_TEXT_LINE => CloseWithoutHidingDisconnect,
                _ => NoInstructions
            });

            // One line, then block until the stream is closed during teardown.
            var harness = Harness.Create(adapter, _ => Task.FromResult<Stream>(
                new HangingStream("{}\n", new IOException(sentinel))));

            await harness.RunIteration();

            // Unobserved exceptions are only raised when the task is finalized.
            for (var i = 0; i < 5 && unobserved.IsEmpty; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(100);
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }

        Assert.True(unobserved.IsEmpty,
            $"The abandoned read faulted with {unobserved.Count} unobserved exception(s): " +
            string.Join(", ", unobserved));
    }

    /// <summary>
    /// `hide_disconnect` must survive parsing: it is what tells the outer loop
    /// to restart immediately instead of waiting out the retry delay.
    /// Surfaces whenever the core asks for a seamless restart: token refresh or subscription change.
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task HideDisconnectRequestsImmediateRestart()
    {
        var adapter = new ScriptedAdapter((op, _) => op switch
        {
            PowerSyncControlCommand.START => EstablishOnly,
            PowerSyncControlCommand.CONNECTION_STATE => @"[{""CloseSyncStream"":{""hide_disconnect"":true}}]",
            _ => NoInstructions
        });

        var harness = Harness.Create(adapter, _ => Task.FromResult<Stream>(new HangingStream("")));

        var immediateRestart = await harness.RunIteration();

        Assert.True(immediateRestart,
            "Expected CloseSyncStream(hide_disconnect: true) to request an immediate restart.");
    }

    // ---- harness -----------------------------------------------------------

    private sealed class Harness
    {
        private readonly TestSyncImplementation sync;
        private readonly CancellationTokenSource cts = new();

        private Harness(TestSyncImplementation sync) => this.sync = sync;

        public static Harness Create(ScriptedAdapter adapter, Func<SyncStreamOptions, Task<Stream>> openStream)
        {
            return new Harness(new TestSyncImplementation(new StreamingSyncImplementationOptions
            {
                Adapter = adapter,
                Remote = new ScriptedRemote(new StaticConnector(), openStream),
                Subscriptions = [],
                UploadCrud = () => Task.CompletedTask
            }));
        }

        public Task<bool?> RunIteration() => sync.RunIteration(cts.Token);

        public void Cancel() => cts.Cancel();
    }

    /// <summary>Exposes the protected iteration so a single pass can be asserted.</summary>
    private sealed class TestSyncImplementation(StreamingSyncImplementationOptions options)
        : StreamingSyncImplementation(options)
    {
        public async Task<bool?> RunIteration(CancellationToken token)
        {
            var result = await RustStreamingSyncIteration(token, DEFAULT_STREAM_CONNECTION_OPTIONS);
            return result.ImmediateRestart;
        }
    }

    /// <summary>Records every powersync_control op and replies with canned instructions.</summary>
    private sealed class ScriptedAdapter(Func<string, object?, string> respond) : IBucketStorageAdapter
    {
        private readonly ConcurrentQueue<string> ops = new();

        public IEnumerable<string> Ops => ops;

        public BucketStorageEvents Events { get; } = new();

        public Task<string> Control(string op, object? payload)
        {
            ops.Enqueue(op);
            return Task.FromResult(respond(op, payload));
        }

        public Task<CrudEntry?> NextCrudItem() => Task.FromResult<CrudEntry?>(null);
        public Task<bool> HasCrud() => Task.FromResult(false);
        public Task<CrudBatch?> GetCrudBatch(int limit = 100) => Task.FromResult<CrudBatch?>(null);
        public Task<bool> UpdateLocalTarget(Func<Task<long>> callback) => Task.FromResult(false);
        public Task HandleCrudCheckpoint(long lastClientId, long? writeCheckpoint = null) => Task.CompletedTask;
        public Task<string> GetClientId() => Task.FromResult("test-client");
        public void Close() { }
    }

    private sealed class ScriptedRemote(IPowerSyncBackendConnector connector, Func<SyncStreamOptions, Task<Stream>> open)
        : Remote(connector)
    {
        public override Task<Stream> PostStreamRaw(SyncStreamOptions options) => open(options);
    }

    private sealed class StaticConnector : IPowerSyncBackendConnector
    {
        public Task<PowerSyncCredentials?> FetchCredentials() =>
            Task.FromResult<PowerSyncCredentials?>(new PowerSyncCredentials("https://powersync.example.org", "test"));

        public Task UploadData(IPowerSyncDatabase database) => Task.CompletedTask;
    }

    // ---- streams -----------------------------------------------------------

    /// <summary>
    /// Serves <paramref name="prefix"/>, then blocks. A pending read completes
    /// with <paramref name="failWith"/> when the stream is closed, mimicking a
    /// socket torn down underneath an in-flight read.
    /// </summary>
    private class HangingStream(string prefix, Exception? failWith = null) : Stream
    {
        private readonly byte[] head = Encoding.UTF8.GetBytes(prefix);
        private readonly TaskCompletionSource<int> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int offset;

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var served = ServeHead(buffer.AsSpan(offset, count));
            return served > 0 ? Task.FromResult(served) : ReadWhenDrained();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var served = ServeHead(buffer.Span);
            return served > 0
                ? new ValueTask<int>(served)
                : new ValueTask<int>(ReadWhenDrained());
        }

        /// <summary>What a read does once the prefix has been consumed.</summary>
        protected virtual Task<int> ReadWhenDrained() => pending.Task;

        private int ServeHead(Span<byte> destination)
        {
            var remaining = head.Length - offset;
            if (remaining <= 0)
            {
                return 0;
            }

            var count = Math.Min(remaining, destination.Length);
            head.AsSpan(offset, count).CopyTo(destination);
            offset += count;
            return count;
        }

        protected override void Dispose(bool disposing)
        {
            Unblock();
            base.Dispose(disposing);
        }

        public override void Close()
        {
            Unblock();
            base.Close();
        }

        private void Unblock()
        {
            if (failWith != null)
            {
                pending.TrySetException(failWith);
            }
            else
            {
                pending.TrySetResult(0);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Serves a prefix, then fails the next read immediately.</summary>
    private sealed class FailAfterPrefixStream(string prefix, Exception failWith) : HangingStream(prefix)
    {
        protected override Task<int> ReadWhenDrained() => Task.FromException<int>(failWith);
    }
}
