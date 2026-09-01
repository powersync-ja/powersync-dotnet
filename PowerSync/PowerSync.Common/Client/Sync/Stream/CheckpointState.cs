namespace PowerSync.Common.Client.Sync.Stream;

using System.Runtime.ExceptionServices;
using System.Threading.Channels;

/// <summary>
/// Tracks whether the active download iteration has reconciled checkpoint request state with the
/// PowerSync service, gating checkpoint requests until it has.
/// </summary>
internal sealed class CheckpointStateSignals
{
    private readonly object gate = new();

    private CheckpointState state = new CheckpointState.Pending();

    /// <summary>
    /// One entry per caller currently blocked in <see cref="WaitForCheckpointRequestsReady"/>.
    /// Completed by <see cref="SetState"/> so every waiter observes each transition.
    /// </summary>
    private readonly List<TaskCompletionSource<bool>> stateWaiters = [];

    /// <summary>
    /// Signalled when a caller starts waiting for checkpoint requests to become available. Used to
    /// resume a download iteration that is currently sitting in its retry delay.
    /// </summary>
    private Channel<bool> checkpointWaiterArrived = CreateNotifier();

    /// <summary>
    /// Marks the current download iteration as ended, blocking new checkpoint requests until the
    /// seed performed by the next iteration completes.
    /// </summary>
    public void DownloadIterationEnded()
    {
        lock (gate)
        {
            // Waiters arriving after this should be able to resume the next download iteration.
            checkpointWaiterArrived = CreateNotifier();
            SetState(new CheckpointState.Pending());
        }
    }

    /// <summary>
    /// Marks the sync client as disconnected, failing all outstanding checkpoint requests and
    /// preventing new ones.
    /// </summary>
    public void Disconnected()
    {
        lock (gate)
        {
            SetState(new CheckpointState.Disconnected());
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
            lock (gate)
            {
                SetState(new CheckpointState.Failed(ex));
            }
            throw;
        }

        lock (gate)
        {
            SetState(new CheckpointState.Ready());
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
        lock (gate)
        {
            reader = checkpointWaiterArrived.Reader;
        }

        await reader.ReadAsync(signal);
    }

    /// <summary>
    /// Waits until a download iteration is active and has seeded the checkpoint state, meaning that
    /// checkpoint request ids can safely be allocated.
    /// </summary>
    /// <param name="signal">Cancelled when the sync client disconnects.</param>
    /// <param name="wakeDownloadLoop">
    /// Whether a paused download loop should be resumed to seed the state. Callers that only want to
    /// piggyback on an iteration someone else needs should pass false.
    /// </param>
    /// <exception cref="CheckpointRequestException">
    /// Thrown when the client is disconnected, or when seeding the checkpoint state failed.
    /// </exception>
    public async Task WaitForCheckpointRequestsReady(CancellationToken signal, bool wakeDownloadLoop = true)
    {
        while (true)
        {
            signal.ThrowIfCancellationRequested();

            TaskCompletionSource<bool> waiter;
            lock (gate)
            {
                switch (state)
                {
                    case CheckpointState.Ready:
                        return;
                    case CheckpointState.Disconnected:
                        throw CheckpointRequestException.Disconnected;
                    case CheckpointState.Failed failed:
                        ExceptionDispatchInfo.Capture(failed.Exception).Throw();
                        return;
                }

                // Pending: wait for the next transition, optionally asking the download loop to start
                // an iteration which can seed the state we're waiting for.
                waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                stateWaiters.Add(waiter);

                if (wakeDownloadLoop)
                {
                    checkpointWaiterArrived.Writer.TryWrite(true);
                }
            }

            using var registration = signal.Register(() => waiter.TrySetCanceled(signal));
            try
            {
                await waiter.Task;
            }
            finally
            {
                lock (gate)
                {
                    stateWaiters.Remove(waiter);
                }
            }
        }
    }

    private void SetState(CheckpointState next)
    {
        state = next;

        foreach (var waiter in stateWaiters)
        {
            waiter.TrySetResult(true);
        }
    }

    /// <summary>A conflating single-slot channel: only the fact that a signal arrived matters.</summary>
    private static Channel<bool> CreateNotifier() =>
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
}

internal abstract record CheckpointState
{
    private CheckpointState() { }

    /// <summary>No iteration has seeded the checkpoint state, requests have to wait.</summary>
    public sealed record Pending : CheckpointState;

    /// <summary>The sync client is disconnected, requests cannot be made at all.</summary>
    public sealed record Disconnected : CheckpointState;

    /// <summary>The active iteration has seeded its state, requests can be made.</summary>
    public sealed record Ready : CheckpointState;

    /// <summary>Seeding the checkpoint state failed.</summary>
    public sealed record Failed(Exception Exception) : CheckpointState;
}
