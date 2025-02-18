namespace Common.Client.Sync.Stream;

using Common.Client.Sync.Bucket;
using Common.DB.Crud;
using Newtonsoft.Json;

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

// TODO CL make these required
public class StreamingSyncImplementationOptions : AdditionalConnectionOptions
{
    public required IBucketStorageAdapter Adapter { get; init; }
    public Func<Task> UploadCrud { get; init; }
    public required Remote Remote { get; init; }
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
    // /// </summary>
    // public bool IsConnected { get; protected set; }

    // /// <summary>
    // /// The timestamp of the last successful sync.
    // /// </summary>
    // public DateTime? LastSyncedAt { get; set; }

    // /// <summary>
    // /// The current synchronization status.
    // /// </summary>
    // public SyncStatus SyncStatus { get; set; }

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
}

public class Logger
{
    public void Debug(string message)
    {
        Console.WriteLine($"[DEBUG] {DateTime.UtcNow:O} - {message}");
    }

    public void Warn(string message)
    {
        Console.WriteLine($"[WARN] {DateTime.UtcNow:O} - {message}");
    }
}



public class StreamingSyncImplementation : IStreamingSyncImplementation
{
    public static readonly int DEFAULT_CRUD_UPLOAD_THROTTLE_MS = 1000;
    public static readonly int DEFAULT_RETRY_DELAY_MS = 5000;

    protected StreamingSyncImplementationOptions options { get; }

    protected CancellationTokenSource? CancellationTokenSource { get; set; }

    private Task? streamingSyncTask;
    private Action internalTriggerCrudUpload;

    private readonly Logger logger = new Logger();

    public StreamingSyncImplementation(StreamingSyncImplementationOptions options)
    {
        this.options = options;
        SyncStatus = new SyncStatus(new SyncStatusOptions
        {
            Connected = false,
            Connecting = false,
            LastSyncedAt = null,
            DataFlow = new SyncDataFlowStatus
            {
                Uploading = false,
                Downloading = false

            }
        });

        CancellationTokenSource = null;

        // TODO CL throttling
        internalTriggerCrudUpload = () =>
        {
            if (!SyncStatus.Connected || SyncStatus.DataFlowStatus.Uploading)
            {
                return;
            }
            InternalUploadAllCrud();
        };
    }

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

    public async Task Connect(PowerSyncConnectionOptions? options = null)
    {
        if (CancellationTokenSource != null)
        {
            await Disconnect();
        }
        CancellationTokenSource = new CancellationTokenSource();


        streamingSyncTask = StreamingSync(this.CancellationTokenSource.Token, options);
    }



    public async Task Disconnect()
    {
        if (CancellationTokenSource == null)
        {
            return;
        }
        // This might be called multiple times
        if (!CancellationTokenSource.Token.IsCancellationRequested)
        {
            CancellationTokenSource.Cancel();
        }

        // Await any pending operations before completing the disconnect operation
        try
        {
            if (streamingSyncTask != null)
            {
                await streamingSyncTask;
            }
        }
        catch (Exception ex)
        {
            // The operation might have failed, all we care about is if it has completed
            logger.Warn(ex.Message);
        }

        streamingSyncTask = null;
        CancellationTokenSource = null;

        UpdateSyncStatus(new SyncStatusOptions { Connected = false, Connecting = false });
    }

    private async Task StreamingSync(CancellationToken? signal, PowerSyncConnectionOptions? options)
    {

        if (signal == null)
        {
            CancellationTokenSource = new CancellationTokenSource();
            signal = CancellationTokenSource.Token;
            // this.CancellationTokenSource.Token.IsCancellationRequested
        }


        // TODO CL listener package
        /**
        * Listen for CRUD updates and trigger upstream uploads
        */
        // this.crudUpdateListener = this.options.adapter.registerListener({
        //   crudUpdate: () => this.triggerCrudUpload()
        // });



        /// This loops runs until [retry] is false or the abort signal is set to aborted.
        /// Aborting the nestedAbortController will:
        /// - Abort any pending fetch requests
        /// - Close any sync stream ReadableStreams (which will also close any established network requests)
        while (true)
        {
            UpdateSyncStatus(new SyncStatusOptions { Connecting = true });

            try
            {
                if (signal.Value.IsCancellationRequested)
                {
                    break;
                }
                // use nestedAbortController.
                var iterationResult = await StreamingSyncIteration(signal.Value, options);
                if (!iterationResult.Retry)
                {

                    // A sync error ocurred that we cannot recover from here.
                    // This loop must terminate.
                    // The nestedAbortController will close any open network requests and streams below.
                    break;
                }
                // Continue immediately
            }
            catch (Exception ex)
            {
                /**
                * Either:
                *  - A network request failed with a failed connection or not OKAY response code.
                *  - There was a sync processing error.
                * This loop will retry.
                * The nested abort controller will cleanup any open network requests and streams.
                * The WebRemote should only abort pending fetch requests or close active Readable streams.
                */
                // On error, wait a little before retrying
                await this.DelayRetry();
            }
            finally
            {

                if (!signal.Value.IsCancellationRequested)
                {
                    // nestedAbortController.abort(new AbortOperation('Closing sync stream network requests before retry.'));
                    // nestedAbortController = new AbortController();
                }

                UpdateSyncStatus(new SyncStatusOptions
                {
                    Connected = false,
                    Connecting = true // May be unnecessary
                });
            }
        }

        // Mark as disconnected if here
        UpdateSyncStatus(new SyncStatusOptions
        {
            Connected = false,
            Connecting = false
        });
    }

    protected record StreamingSyncIterationResult(bool Retry);
    // protected async streamingSyncIteration
    protected async Task<StreamingSyncIterationResult> StreamingSyncIteration(CancellationToken signal, PowerSyncConnectionOptions? options)
    {

        return new StreamingSyncIterationResult(true);
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
    public record ResponseData(
        [property: JsonProperty("write_checkpoint")] string WriteCheckpoint
    );

    public record ApiResponse(
        [property: JsonProperty("data")] ResponseData Data
    );
    public async Task<string> GetWriteCheckpoint()
    {
        var clientId = await options.Adapter.GetClientId();
        var path = $"/write-checkpoint2.json?client_id={clientId}";
        var response = await options.Remote.Get<ApiResponse>(path);

        return response.Data.WriteCheckpoint;
    }

    // TODO CL Locking
    protected Task InternalUploadAllCrud()
    {
        throw new NotImplementedException();
    }

    public Task<bool> HasCompletedSync()
    {
        throw new NotImplementedException();
    }

    public void TriggerCrudUpload()
    {
        internalTriggerCrudUpload();
    }

    public Task WaitForReady()
    {
        throw new NotImplementedException();
    }

    public Task WaitForStatus(SyncStatusOptions status)
    {
        throw new NotImplementedException();
    }



    void IDisposable.Dispose()
    {
        throw new NotImplementedException();
    }

    protected void UpdateSyncStatus(SyncStatusOptions options)
    {
        var updatedStatus = new SyncStatus(new SyncStatusOptions
        {
            Connected = options.Connected ?? SyncStatus.Connected,
            Connecting = !options.Connected.GetValueOrDefault() && (options.Connecting ?? SyncStatus.Connecting),
            LastSyncedAt = options.LastSyncedAt ?? SyncStatus.LastSyncedAt,
            DataFlow = new SyncDataFlowStatus
            {
                Uploading = options.DataFlow?.Uploading ?? this.SyncStatus.DataFlowStatus.Uploading,
                Downloading = options.DataFlow?.Downloading ?? this.SyncStatus.DataFlowStatus.Downloading
            }
        });

        if (!SyncStatus.Equals(updatedStatus))
        {
            SyncStatus = updatedStatus;
            // Only trigger this if there was a change
            // IterateListeners(cb => cb.StatusChanged?.Invoke(updatedStatus));
        }

        // Trigger this for all updates
        // IterateListeners(cb => cb.StatusUpdated?.Invoke(options));
    }

    private async Task DelayRetry()
    {
        if (options.RetryDelayMs.HasValue)
        {
            await Task.Delay(options.RetryDelayMs.Value);
        }
    }
}