namespace PowerSync.Common.Client.Connection;

/// <summary>
/// Authenticates the PowerSync SDK against a PowerSync service, allowing it to download changes.
/// </summary>
public interface IAuthenticator
{
    /// <summary>
    /// Resolves a JWT to use when connecting to a PowerSync service.
    ///
    /// The SDK does not cache this value and will call this method multiple times while a database
    /// is connected. Implementations should consider caching credentials.
    /// </summary>
    public Task<string> ResolveCredentials();

    /// <summary>
    /// Invoked by the SDK when the PowerSync service has rejected credentials previously returned by
    /// <see cref="ResolveCredentials" />.
    ///
    /// If the connector returns cached tokens, it can use this as a hint to refresh its local state.
    /// </summary>
    public void InvalidateCredentials() { }
}

/// <summary>
/// Uploads local mutations (from `INSERT`, `UPDATE` and `DELETE` statements against the local
/// database) to your source database.
/// 
/// While simple cases might use a protocol like PostgREST to write directly into the database,
/// a custom backend is commonly used to validate uploaded mutations.
/// </summary>
public interface IMutationUploader
{
    public Task UploadMutations(IPowerSyncDatabase database);
}

public interface IPowerSyncBackendConnector : IAuthenticator, IMutationUploader
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
    public Task<PowerSyncCredentials?> FetchCredentials();

    /// <summary> 
    /// Upload local changes to the app backend.
    ///
    /// Use <see cref="IPowerSyncDatabase.GetCrudBatch" /> to get a batch of changes to upload.
    ///
    /// Any thrown errors will result in a retry after the configured wait period (default: 5 seconds).
    /// </summary>
    public Task UploadData(IPowerSyncDatabase database);

    async Task<string> IAuthenticator.ResolveCredentials()
    {
        var credentials = await FetchCredentials() ?? throw new InvalidOperationException("User is not logged in");
        return credentials.Token;
    }

    Task IMutationUploader.UploadMutations(IPowerSyncDatabase database) => UploadData(database);
}

/// <summary>
/// An <see cref="IAuthenticator" /> capable of requesting checkpoints.
///
/// Implement this interface instead of <see cref="IAuthenticator" /> when uploads are processed
/// asynchronously by the backend (for example through a message queue): The sync client as part of
/// the PowerSync .NET SDK generates a checkpoint request id and hands it to your backend via this
/// class, which is responsible for creating a matching checkpoint once the uploads preceding the
/// request have been processed.
/// For more details, see <see href="https://docs.powersync.com/client-sdks/advanced/checkpoint-requests#asynchronous-upload-backends">asynchronous backend uploads</see>.
///
/// To use this connector, using <see cref="Sync.Stream.CheckpointMode.Requests" /> is required. Note that
/// this requires PowerSync service version 1.24.0 or later.
/// </summary>
public interface ICustomCheckpointRequestConnector : IAuthenticator
{
    /// <summary>
    /// Posts a client-generated checkpoint request to the backend and returns the effective checkpoint request state.
    /// </summary>
    Task<long> PostCheckpointRequest(string clientId, long requestId, CancellationToken token);
}
