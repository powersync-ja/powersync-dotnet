// TODO CheckpointStateTests.cs
using PowerSync.Common.Utils;

namespace PowerSync.Common.Client.Sync.Stream;

internal class CheckpointStateSignals
{
    private CheckpointState _state = new CheckpointState.Pending();
    private readonly BroadcastChannel<CheckpointState> _stateBroadcaster = new();
    private TaskCompletionSource<bool> _waitingForCheckpointsReady = new();

    // -- Check behaviour
    private void UpdateState(CheckpointState state)
    {
        // TODO Run this asynchronously in another Task?
        _state = state;
        _stateBroadcaster.Broadcast(state);
    }

    /// <summary>
    /// Marks the current download iteration as ended, blocking new checkpoint requests until the
    /// seed was performed in the next iteration.
    /// </summary>
    public void DownloadIterationEnded()
    {
        _waitingForCheckpointsReady = new();
        UpdateState(new CheckpointState.Pending());
    }

    /// <summary>
    /// Marks the sync client as disconnected, failing all outstanding checkpoint
    /// requests and preventing new ones.
    /// </summary>
    public void Disconnect()
    {
        UpdateState(new CheckpointState.Disconnected());
    }

    /// Waits for a waiter wanting torequest a checkpoint.
    ///
    /// As the waiter is blocked for a seed run we start in the download
    /// iteration we use this to wake up the download iteration if it's currently
    /// paused.
    public Task WaitForCheckpointWaiter() => _waitingForCheckpointsReady.Task;

    public void MarkCheckpointsReady()
    {
        UpdateState(new CheckpointState.Ready());
    }

    public void MarkCheckpointsFailed(Exception ex)
    {
        UpdateState(new CheckpointState.Error(ex));
    }

    public Task WaitForCheckpointRequestsReady(CancellationToken signal, bool wakeDownloadLoop = true)
    {
        var tcs = new TaskCompletionSource<bool>();
        var reader = _stateBroadcaster.Subscribe(out var subscriberId);

        void UnsubscribeReader() => _stateBroadcaster.Unsubscribe(subscriberId);

        // Resolves the promise from the current state if possible, returning true if it was
        bool HandleState(CheckpointState state)
        {
            switch (state)
            {
                case CheckpointState.Disconnected:
                    tcs.TrySetException(CheckpointRequestException.Disconnected);
                    UnsubscribeReader();
                    return true;

                case CheckpointState.Ready:
                    tcs.TrySetResult(true);
                    UnsubscribeReader();
                    return true;

                case CheckpointState.Error e:
                    tcs.TrySetException(e.Exception);
                    UnsubscribeReader();
                    return true;

                case CheckpointState.Pending:
                    if (wakeDownloadLoop)
                    {
                        _waitingForCheckpointsReady.TrySetResult(true);
                    }
                    return false;
            }
            return false;
        }

        // Listen to state changes until task resolves
        var cts = CancellationTokenSource.CreateLinkedTokenSource(signal);
        _ = Task.Run(async () =>
        {
            while (reader.TryRead(out var state))
            {
                if (HandleState(state))
                {
                    cts.Cancel();
                }
            }
        }, cts.Token);
        HandleState(_state);

        return tcs.Task;
        // TODO CHECK IF THIS WORKS
    }
}

internal record CheckpointState
{
    private CheckpointState() { }

    public sealed record Pending : CheckpointState;
    public sealed record Disconnected : CheckpointState;
    public sealed record Ready : CheckpointState;
    public sealed record Error(Exception Exception) : CheckpointState;
}
