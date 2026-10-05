using PowerSync.Common.Client.Sync.Stream;

namespace PowerSync.Common.Client.Sync;

/// <summary>
/// A checkpoint request created by <see cref="PowerSyncDatabase.RequestCheckpoint" />.
///
/// Use this value to wait until the local database has applied server-side changes up to the
/// requested checkpoint. This is useful for explicit refresh flows where the caller wants
/// confirmation that the local view has caught up to the service.
///
/// Checkpoint requests are backed by request ids tracked in the local database, so they are reusable
/// across disconnect and reconnect cycles. A wait interrupted by a disconnect throws an error, but
/// the same request can be awaited again once a new connection is established.
///
/// Requests do not survive <see cref="PowerSyncDatabase.DisconnectAndClear" />, instances created
/// before a clear should be discarded and requested again.
/// </summary>
public class CheckpointRequest
{
    private readonly long _requestId;
    private readonly PowerSyncDatabase _db;

    internal CheckpointRequest(long requestId, PowerSyncDatabase db)
    {
        _requestId = requestId;
        _db = db;
    }

    ///<summary>Whether this checkpoint request has synced before.</summary>
    public bool HasSynced { get => _db.SyncStreamImplementation?.IsCheckpointRequestApplied(_requestId) ?? false; }

    /// <summary>
    /// Waits until this checkpoint has been synced locally.
    ///
    /// This method fails on sync errors: If a download or upload error occurs before this checkpoint
    /// request has synced, that error is rethrown here. This makes it easier to observe sync errors
    /// when relying on checkpoints. Once sync has recovered, it is valid to call this method again
    /// to await the checkpoint.
    /// </summary>
    /// <exception cref="CheckpointRequestException" />
    /// <exception cref="OperationCanceledException">
    /// Thrown if the cancellation token is canceled. Importantly, this is not thrown if the checkpoint
    /// has already finished syncing, as there is no work to be canceled.
    /// </exception>
    public Task WaitForSync(CancellationToken ct = default)
    {
        if (HasSynced) return Task.CompletedTask;

        ct.ThrowIfCancellationRequested();

        var sync = _db.SyncStreamImplementation;

        if (sync is null)
        {
            throw new CheckpointRequestException(CheckpointRequestException.Disconnected);
        }

        if (sync.ConnectionOptions?.CheckpointMode == CheckpointMode.Legacy)
        {
            throw new CheckpointRequestException(CheckpointRequestException.Disabled);
        }

        return _db.WaitForStatus(status =>
        {
            if (HasSynced)
            {
                return true;
            }

            Exception? anyError = status.DataFlowStatus.DownloadError ?? status.DataFlowStatus.UploadError;
            if (anyError is not null)
            {
                throw new CheckpointRequestException(CheckpointRequestException.StatusError, anyError);
            }

            if (!status.Connected && !status.Connecting)
            {
                throw new CheckpointRequestException(CheckpointRequestException.Disconnected);
            }

            return false;
        }, ct);
    }
}

/// <summary>An exception related to checkpoint requests.</summary>
public class CheckpointRequestException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CheckpointRequestException" /> class.</summary>
    public CheckpointRequestException() : base() { }

    /// <summary>Initializes a new instance of the <see cref="CheckpointRequestException" /> class with a specified error message.</summary>
    public CheckpointRequestException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="CheckpointRequestException" /> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
    public CheckpointRequestException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>The connected PowerSync Service does not support checkpoint requests.</summary>
    public static readonly string InstanceNotSupported = "The PowerSync service does not support checkpoint requests. Update to PowerSync service version 1.24.0 or later to use this API.";

    /// <summary>The sync client is disconnected.</summary>
    public static readonly string Disconnected = "Cannot request checkpoints, sync client is disconnected";

    /// <summary>Checkpoint requests are disabled; legacy write checkpoints are enabled.</summary>
    public static readonly string Disabled = "Connected with legacy checkpoint mode, cannot request checkpoints";

    /// <summary></summary>
    public static readonly string StatusError = "Error on sync status before checkpoint was applied";
}
