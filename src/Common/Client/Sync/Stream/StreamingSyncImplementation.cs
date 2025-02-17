using Common.Client.Sync.Bucket;
using Common.DB.Crud;

namespace Common.Client.Sync.Stream;

public class AdditionalConnectionOptions(int? retryDelayMs = null, int? crudUploadThrottleMs = null)
{
    /// <summary>
    /// Delay for retrying sync streaming operations
    /// from the PowerSync backend after an error occurs.
    /// </summary>
    public int? RetryDelayMs { get; set; } = retryDelayMs;

    /// <summary>
    /// Backend Connector CRUD operations are throttled
    /// to occur at most every `CrudUploadThrottleMs`
    /// milliseconds.
    /// </summary>
    public int? CrudUploadThrottleMs { get; set; } = crudUploadThrottleMs;
}

public class StreamingSyncImplementationOptions(
    IBucketStorageAdapter adapter,
    Func<Task> uploadCrud,
    Remote remote,
    int? retryDelayMs = null,
    int? crudUploadThrottleMs = null
) : AdditionalConnectionOptions(retryDelayMs, crudUploadThrottleMs)
{
    public IBucketStorageAdapter Adapter { get; } = adapter;

    public Func<Task> UploadCrud { get; } = uploadCrud;

    public Remote Remote { get; } = remote;
}

public class BaseConnectionOptions(Dictionary<string, object>? parameters = null)
{
    /// <summary>
    /// These parameters are passed to the sync rules and will be available under the `user_parameters` object.
    /// </summary>
    public Dictionary<string, object>? Params { get; set; } = parameters;
}

public class PowerSyncConnectionOptions(
    Dictionary<string, object>? @params = null,
    int? retryDelayMs = null,
    int? crudUploadThrottleMs = null
) : BaseConnectionOptions(@params)
{
    /// <summary>
    /// Delay for retrying sync streaming operations from the PowerSync backend after an error occurs.
    /// </summary>
    public int? RetryDelayMs { get; set; } = retryDelayMs;

    /// <summary>
    /// Backend Connector CRUD operations are throttled to occur at most every `CrudUploadThrottleMs` milliseconds.
    /// </summary>
    public int? CrudUploadThrottleMs { get; set; } = crudUploadThrottleMs;
}

public interface IStreamingSyncImplementation : IDisposable
{
    /// <summary>
    /// Indicates if the sync service is connected.
    /// </summary>
    public bool IsConnected { get; protected set; }

    /// <summary>
    /// The timestamp of the last successful sync.
    /// </summary>
    public DateTime? LastSyncedAt { get; protected set; }

    /// <summary>
    /// The current synchronization status.
    /// </summary>
    public SyncStatus SyncStatus { get; protected set; }

    /// <summary>
    /// Connects to the sync service.
    /// </summary>
    public abstract Task Connect(PowerSyncConnectionOptions? options = null);

    /// <summary>
    /// Disconnects from the sync service.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if not connected or if abort is not controlled internally.</exception>
    public abstract Task Disconnect();

    /// <summary>
    /// Gets the current write checkpoint.
    /// </summary>
    public abstract Task<string> GetWriteCheckpoint();

    /// <summary>
    /// Checks whether the sync has completed.
    /// </summary>
    public abstract Task<bool> HasCompletedSync();

    /// <summary>
    /// Triggers the upload of pending CRUD operations.
    /// </summary>
    public abstract void TriggerCrudUpload();

    /// <summary>
    /// Waits until the sync service is fully ready.
    /// </summary>
    public abstract Task WaitForReady();

    /// <summary>
    /// Waits until the sync status matches the specified status.
    /// </summary>
    public abstract Task WaitForStatus(SyncStatusOptions status);

    /// <summary>
    /// Disposes the instance.
    /// </summary>
    public abstract void Dispose();
}

public class StreamingSyncImplementation
{
    StreamingSyncImplementation()
    {

    }
}