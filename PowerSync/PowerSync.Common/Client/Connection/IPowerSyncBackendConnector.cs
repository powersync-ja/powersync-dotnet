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
