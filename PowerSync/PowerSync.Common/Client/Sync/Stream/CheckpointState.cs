namespace PowerSync.Common.Client.Sync.Stream;

using System.Runtime.ExceptionServices;
using System.Threading.Channels;

using PowerSync.Common.Utils;

/// <summary>
/// Tracks whether the active download iteration has reconciled checkpoint request state with the
/// PowerSync service, gating checkpoint requests until it has.
/// </summary>
internal sealed class CheckpointStateSignals
{
    private CheckpointState _state = new CheckpointState.Pending();

    private readonly BroadcastChannel<bool> _stateBroadcaster = new();
    private Channel<bool> _checkpointWaiterNotifier = CreateNotifier();

    private readonly object _lock = new();

    /// <summary>
    /// Marks the current download iteration as ended, blocking new checkpoint requests until the
    /// seed performed by the next iteration completes.
    /// </summary>
    public void DownloadIterationEnded()
    {
        lock (_lock)
        {
            // Waiters arriving after this should be able to resume the next download iteration.
            _checkpointWaiterNotifier = CreateNotifier();
            UpdateState(new CheckpointState.Pending());
        }
    }

    /// <summary>
    /// Marks the sync client as disconnected, failing all outstanding checkpoint requests and
    /// preventing new ones.
    /// </summary>
    public void Disconnected()
    {
        lock (_lock)
        {
            UpdateState(new CheckpointState.Disconnected());
        }
    }

    /// <summary>
    /// Runs <paramref name="seed"/>, publishing its outcome to callers of
    /// <see cref="WaitForCheckpointRequestsReady"/>. Cancellation leaves the state pending, since a
    /// later iteration will seed it again.
    /// </summary>
    public async Task MarkCheckpointsReady(Func<Task> seed)
    {
        try
        {
            await seed();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                UpdateState(new CheckpointState.Failed(ex));
            }
            throw;
        }

        lock (_lock)
        {
            UpdateState(new CheckpointState.Ready());
        }
    }

    /// <summary>
    /// Waits for a caller wanting to request a checkpoint.
    /// <para />
    /// That caller is blocked until the seed run started by a download iteration completes, so this
    /// is used to wake up the download loop while it is paused between iterations.
    /// </summary>
    public async Task WaitForCheckpointWaiter(CancellationToken signal)
    {
        ChannelReader<bool> reader;
        lock (_lock)
        {
            reader = _checkpointWaiterNotifier.Reader;
        }

        await reader.ReadAsync(signal);
    }

    /// <summary>
    /// Waits until a download iteration is active and has seeded the checkpoint state, meaning that
    /// checkpoint request ids can safely be allocated.
    /// </summary>
    public async Task WaitForCheckpointRequestsReady(CancellationToken signal, bool wakeDownloadLoop = true)
    {
        var reader = _stateBroadcaster.Subscribe(out var subscriberId);
        try
        {
            // Only the first check may wake the download loop. A waiter that is already parked when
            // an iteration ends must not resume the next one, otherwise a download loop that keeps
            // failing would retry without ever waiting out its retry delay.
            var wake = wakeDownloadLoop;

            while (!HandleState(wake))
            {
                wake = false;
                await reader.ReadAsync(signal);
            }
        }
        finally
        {
            _stateBroadcaster.Unsubscribe(subscriberId);
        }
    }

    /// <summary>
    /// Returns true if checkpoint requests are ready and false if we need
    /// to keep waiting.
    /// </summary>
    private bool HandleState(bool wakeDownloadLoop)
    {
        lock (_lock)
        {
            switch (_state)
            {
                case CheckpointState.Ready:
                    return true;
                case CheckpointState.Disconnected:
                    throw new CheckpointRequestException(CheckpointRequestException.Disconnected);
                case CheckpointState.Failed failed:
                    ExceptionDispatchInfo.Capture(failed.Exception).Throw();
                    return true;
                case CheckpointState.Pending:
                    if (wakeDownloadLoop)
                    {
                        _checkpointWaiterNotifier.Writer.TryWrite(true);
                    }
                    return false;
                default:
                    throw new InvalidOperationException($"Invalid CheckpointState: {_state}");
            }
        }
    }

    private void UpdateState(CheckpointState next)
    {
        _state = next;
        _stateBroadcaster.Broadcast(true);
    }

    /// <summary>Channel that always holds the latest item written. Used to notify listeners that an event has occured.</summary>
    private static Channel<bool> CreateNotifier() =>
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
}

internal abstract record CheckpointState
{
    private CheckpointState() { }

    public sealed record Pending : CheckpointState;

    public sealed record Disconnected : CheckpointState;

    public sealed record Ready : CheckpointState;

    public sealed record Failed(Exception Exception) : CheckpointState;
}
