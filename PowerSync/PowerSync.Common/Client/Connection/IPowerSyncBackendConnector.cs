namespace PowerSync.Common.Client.Connection;

public interface IPowerSyncBackendConnector
{
    /// <summary> 
    /// Allows the PowerSync client to retrieve an authentication token from your backend
    /// which is used to authenticate against the PowerSync service.
    /// <para /> 
    /// This should always fetch a fresh set of credentials - don't use cached
    /// values.
    /// <para /> 
    /// Return null if the user is not signed in. Throw an error if credentials
    /// cannot be fetched due to a network error or other temporary error.
    ///
    /// This token is kept for the duration of a sync connection.
    /// </summary>
    Task<PowerSyncCredentials?> FetchCredentials();

    /// <summary> 
    /// Upload local changes to the app backend.
    ///
    /// Use <see cref="IPowerSyncDatabase.GetCrudBatch" /> to get a batch of changes to upload.
    ///
    /// Any thrown errors will result in a retry after the configured wait period (default: 5 seconds).
    /// </summary>
    Task UploadData(IPowerSyncDatabase database);
}

/// <summary>
/// An <see cref="IPowerSyncBackendConnector" /> capable of requesting checkpoints.
///
/// Extend this class instead of <see cref="IPowerSyncBackendConnector" /> when uploads are processed
/// asynchronously by the backend (for example through a message queue): The sync client as part of
/// the PowerSync .NET SDK generates a checkpoint request id and hands it to your backend via this
/// class, which is responsible for creating a matching checkpoint once the uploads preceding the
/// request have been processed.
/// For more details, see <see href="https://docs.powersync.com/client-sdks/advanced/checkpoint-requests#asynchronous-upload-backends">asynchronous backend uploads</see>.
///
/// To use this connector, using <see cref="Sync.Stream.CheckpointMode.Requests" /> is required. Note that
/// this requires PowerSync service version 1.24.0 or later.
/// </summary>
public interface ICustomCheckpointRequestConnector : IPowerSyncBackendConnector
{
    /// <summary>
    /// Posts a client-generated checkpoint request to the backend and returns the effective checkpoint request state.
    /// <para />
    /// Currently, checkpoint request IDs are represented as strings. This is because some PowerSync SDKs are for runtimes
    /// that don't have a fast 64-bit integer type. In a future release, checkpoint request IDs will change to be
    /// represented by longs, meaning the <paramref name="requestId" /> parameter's type will also change to `long`.
    /// </summary>
    Task<string> PostCheckpointRequest(string clientId, string requestId, CancellationToken token);
}
