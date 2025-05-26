namespace PowerSync.Common.Client.Sync.Stream;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Newtonsoft.Json;

using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.DB.Crud;
using PowerSync.Common.Utils;

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

public class RequiredAdditionalConnectionOptions : AdditionalConnectionOptions
{
    public static RequiredAdditionalConnectionOptions DEFAULT_ADDITIONAL_CONNECTION_OPTIONS = new()
    {
        CrudUploadThrottleMs = 1000,
        RetryDelayMs = 5000
    };

    public new int RetryDelayMs { get; set; }

    public new int CrudUploadThrottleMs { get; set; }
}

public class StreamingSyncImplementationOptions : AdditionalConnectionOptions
{
    public IBucketStorageAdapter Adapter { get; init; } = null!;
    public Func<Task> UploadCrud { get; init; } = null!;
    public Remote Remote { get; init; } = null!;

    public ILogger? Logger { get; init; }
}

public class BaseConnectionOptions(Dictionary<string, object>? parameters = null)
{
    /// <summary>
    /// These parameters are passed to the sync rules and will be available under the `user_parameters` object.
    /// </summary>
    public Dictionary<string, object>? Params { get; set; } = parameters;
}

public class RequiredPowerSyncConnectionOptions : BaseConnectionOptions
{
    public new Dictionary<string, object> Params { get; set; } = new();
}

public class StreamingSyncImplementationEvent
{
    /// <summary>
    /// Set whenever a status update has been attempted to be made or refreshed.
    /// </summary>
    public SyncStatusOptions? StatusUpdated { get; set; }

    /// <summary>
    /// Set whenever the status' members have changed in value.
    /// </summary>
    public SyncStatus? StatusChanged { get; set; }
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

public class StreamingSyncImplementation : EventStream<StreamingSyncImplementationEvent>
{
    public static RequiredPowerSyncConnectionOptions DEFAULT_STREAM_CONNECTION_OPTIONS = new()
    {
        Params = []
    };

    public static readonly int DEFAULT_CRUD_UPLOAD_THROTTLE_MS = 1000;
    public static readonly int DEFAULT_RETRY_DELAY_MS = 5000;

    protected StreamingSyncImplementationOptions Options { get; }

    protected CancellationTokenSource? CancellationTokenSource { get; set; }

    private Task? streamingSyncTask;
    public Action TriggerCrudUpload { get; }
    private CancellationTokenSource? crudUpdateCts;
    private readonly ILogger logger;

    private readonly StreamingSyncLocks locks;

    public StreamingSyncImplementation(StreamingSyncImplementationOptions options)
    {
        Options = options;
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

        locks = new StreamingSyncLocks();
        logger = options.Logger ?? NullLogger.Instance;

        CancellationTokenSource = null;

        TriggerCrudUpload = () =>
        {
            if (!SyncStatus.Connected || SyncStatus.DataFlowStatus.Uploading)
            {
                return;
            }

            Task.Run(async () => await InternalUploadAllCrud());
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

        streamingSyncTask = StreamingSync(CancellationTokenSource.Token, options);

        var tcs = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();

        var _ = Task.Run(() =>
        {
            foreach (var status in Listen(cts.Token))
            {
                if (status.StatusUpdated != null)
                {
                    if (status.StatusUpdated.Connected != null)
                    {
                        if (status.StatusUpdated.Connected == false)
                        {
                            logger.LogWarning("Initial connect attempt did not successfully connect to server");
                        }

                        tcs.SetResult(true);
                        cts.Cancel();
                    }
                }
            }
        });

        await tcs.Task;
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
            logger.LogWarning("{Message}", ex.Message);
        }

        streamingSyncTask = null;
        CancellationTokenSource = null;

        UpdateSyncStatus(new SyncStatusOptions { Connected = false, Connecting = false });
    }

    protected async Task StreamingSync(CancellationToken? signal, PowerSyncConnectionOptions? options)
    {
        if (signal == null)
        {
            CancellationTokenSource = new CancellationTokenSource();
            signal = CancellationTokenSource.Token;
        }

        crudUpdateCts = new CancellationTokenSource();
        var _ = Task.Run(() =>
        {
            foreach (var _ in Options.Adapter.Listen(crudUpdateCts.Token))
            {
                TriggerCrudUpload();
            }
        });

        // Create a new cancellation token source for nested operations.
        // This is needed to close any previous connections.
        var nestedCts = new CancellationTokenSource();
        signal.Value.Register(() =>
        {
            nestedCts.Cancel();
            crudUpdateCts?.Cancel();
            crudUpdateCts = null;
            UpdateSyncStatus(new SyncStatusOptions
            {
                Connected = false,
                Connecting = false,
                DataFlow = new SyncDataFlowStatus { Downloading = false }
            });
        });

        /// This loops runs until [retry] is false or the abort signal is set to aborted.
        /// Aborting the nestedCts will:
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
                var iterationResult = await StreamingSyncIteration(nestedCts.Token, options);
                if (!iterationResult.Retry)
                {

                    // A sync error ocurred that we cannot recover from here.
                    // This loop must terminate.
                    // The nestedCts will close any open network requests and streams below.
                    break;
                }
                // Continue immediately
            }
            catch (Exception ex)
            {
                logger.LogError("Caught exception in streaming sync: {message}", ex.Message);

                // Either:
                //  - A network request failed with a failed connection or not OKAY response code.
                //  - There was a sync processing error.
                // This loop will retry.
                // The nested abort controller will cleanup any open network requests and streams.
                // The WebRemote should only abort pending fetch requests or close active Readable streams.

                UpdateSyncStatus(new SyncStatusOptions
                {
                    Connected = false,
                    DataFlow = new SyncDataFlowStatus
                    {
                        DownloadError = ex
                    }
                });

                // On error, wait a little before retrying
                await DelayRetry();
            }
            finally
            {

                if (!signal.Value.IsCancellationRequested)
                {
                    // Closing sync stream network requests before retry.
                    nestedCts.Cancel();
                    nestedCts = new CancellationTokenSource();
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

    protected record StreamingSyncIterationResult
    {
        public bool Retry { get; init; }
    }

    protected async Task<StreamingSyncIterationResult> StreamingSyncIteration(CancellationToken signal, PowerSyncConnectionOptions? options)
    {
        return await locks.ObtainLock(new LockOptions<StreamingSyncIterationResult>
        {
            Type = LockType.SYNC,
            Token = signal,
            Callback = async () =>
            {
                var resolvedOptions = new RequiredPowerSyncConnectionOptions
                {
                    Params = options?.Params ?? DEFAULT_STREAM_CONNECTION_OPTIONS.Params
                };

                logger.LogDebug("Streaming sync iteration started");
                Options.Adapter.StartSession();
                var bucketEntries = await Options.Adapter.GetBucketStates();
                var initialBuckets = new Dictionary<string, string>();

                foreach (var entry in bucketEntries)
                {
                    initialBuckets[entry.Bucket] = entry.OpId;
                }

                var req = initialBuckets
                    .Select(kvp => new BucketRequest
                    {
                        Name = kvp.Key,
                        After = kvp.Value
                    })
                    .ToList();

                var targetCheckpoint = (Checkpoint?)null;
                var validatedCheckpoint = (Checkpoint?)null;
                var appliedCheckpoint = (Checkpoint?)null;

                var bucketSet = new HashSet<string>(initialBuckets.Keys);

                var clientId = await Options.Adapter.GetClientId();

                logger.LogDebug("Requesting stream from server");

                var syncOptions = new SyncStreamOptions
                {
                    Path = "/sync/stream",
                    CancellationToken = signal,
                    Data = new StreamingSyncRequest
                    {
                        Buckets = req,
                        IncludeChecksum = true,
                        RawData = true,
                        Parameters = resolvedOptions.Params, // Replace with actual params
                        ClientId = clientId
                    }
                };

                var stream = Options.Remote.PostStream(syncOptions);
                var first = true;
                await foreach (var line in stream)
                {
                    if (first)
                    {
                        first = false;
                        logger.LogDebug("Stream established. Processing events");
                    }

                    if (line == null)
                    {
                        logger.LogDebug("Stream has closed while waiting");
                        // The stream has closed while waiting
                        return new StreamingSyncIterationResult { Retry = true };
                    }

                    // A connection is active and messages are being received
                    if (!SyncStatus.Connected)
                    {
                        // There is a connection now
                        UpdateSyncStatus(new SyncStatusOptions
                        {
                            Connected = true
                        });
                        TriggerCrudUpload();
                    }

                    if (line is StreamingSyncCheckpoint syncCheckpoint)
                    {
                        logger.LogDebug("Sync checkpoint: {message}", syncCheckpoint);

                        targetCheckpoint = syncCheckpoint.Checkpoint;
                        var bucketsToDelete = new HashSet<string>(bucketSet);
                        var newBuckets = new HashSet<string>();

                        foreach (var checksum in syncCheckpoint.Checkpoint.Buckets)
                        {
                            newBuckets.Add(checksum.Bucket);
                            bucketsToDelete.Remove(checksum.Bucket);
                        }
                        if (bucketsToDelete.Count > 0)
                        {
                            logger.LogDebug("Removing buckets: {message}", string.Join(", ", bucketsToDelete));
                        }

                        bucketSet = newBuckets;
                        await Options.Adapter.RemoveBuckets([.. bucketsToDelete]);
                        await Options.Adapter.SetTargetCheckpoint(targetCheckpoint);
                    }
                    else if (line is StreamingSyncCheckpointComplete checkpointComplete)
                    {
                        logger.LogDebug("Checkpoint complete: {message}", targetCheckpoint);

                        var result = await Options.Adapter.SyncLocalDatabase(targetCheckpoint!);

                        if (!result.CheckpointValid)
                        {
                            // This means checksums failed. Start again with a new checkpoint.
                            // TODO: better back-off
                            await Task.Delay(50);
                            return new StreamingSyncIterationResult { Retry = true };
                        }
                        else if (!result.Ready)
                        {
                            // Checksums valid, but need more data for a consistent checkpoint.
                            // Continue waiting.
                            // Landing here the whole time
                        }
                        else
                        {
                            appliedCheckpoint = targetCheckpoint;
                            logger.LogDebug("Validated checkpoint: {message}", appliedCheckpoint);

                            UpdateSyncStatus(new SyncStatusOptions
                            {
                                Connected = true,
                                LastSyncedAt = DateTime.Now,
                                DataFlow = new SyncDataFlowStatus
                                {
                                    Downloading = false
                                }
                            }, new UpdateSyncStatusOptions
                            {
                                ClearDownloadError = true
                            });

                        }

                        validatedCheckpoint = targetCheckpoint;
                    }
                    else if (line is StreamingSyncCheckpointDiff checkpointDiff)
                    {
                        // TODO: It may be faster to just keep track of the diff, instead of the entire checkpoint
                        if (targetCheckpoint == null)
                        {
                            throw new Exception("Checkpoint diff without previous checkpoint");
                        }

                        var diff = checkpointDiff.CheckpointDiff;
                        var newBuckets = new Dictionary<string, BucketChecksum>();

                        foreach (var checksum in targetCheckpoint.Buckets)
                        {
                            newBuckets[checksum.Bucket] = checksum;
                        }

                        foreach (var checksum in diff.UpdatedBuckets)
                        {
                            newBuckets[checksum.Bucket] = checksum;
                        }

                        foreach (var bucket in diff.RemovedBuckets)
                        {
                            newBuckets.Remove(bucket);
                        }

                        var newWriteCheckpoint = !string.IsNullOrEmpty(diff.WriteCheckpoint) ? diff.WriteCheckpoint : null;
                        var newCheckpoint = new Checkpoint
                        {
                            LastOpId = diff.LastOpId,
                            Buckets = [.. newBuckets.Values],
                            WriteCheckpoint = newWriteCheckpoint
                        };

                        targetCheckpoint = newCheckpoint;

                        bucketSet = [.. newBuckets.Keys];

                        var bucketsToDelete = diff.RemovedBuckets.ToArray();
                        if (bucketsToDelete.Length > 0)
                        {
                            logger.LogDebug("Remove buckets: {message}", string.Join(", ", bucketsToDelete));
                        }

                        // Perform async operations
                        await Options.Adapter.RemoveBuckets(bucketsToDelete);
                        await Options.Adapter.SetTargetCheckpoint(targetCheckpoint);
                    }
                    else if (line is StreamingSyncDataJSON dataJSON)
                    {
                        UpdateSyncStatus(new SyncStatusOptions
                        {
                            DataFlow = new SyncDataFlowStatus
                            {
                                Downloading = true
                            }
                        });
                        await Options.Adapter.SaveSyncData(new SyncDataBatch([SyncDataBucket.FromRow(dataJSON.Data)]));
                    }
                    else if (line is StreamingSyncKeepalive keepalive)
                    {
                        var remainingSeconds = keepalive.TokenExpiresIn;
                        if (remainingSeconds == 0)
                        {
                            // Connection would be closed automatically right after this
                            logger.LogDebug("Token expiring; reconnect");
                            Options.Remote.InvalidateCredentials();

                            // For a rare case where the backend connector does not update the token
                            // (uses the same one), this should have some delay.
                            //
                            await DelayRetry();
                            return new StreamingSyncIterationResult { Retry = true };
                        }
                        else if (remainingSeconds < 30)
                        {
                            logger.LogDebug("Token will expire soon; reconnect");
                            // Pre-emptively refresh the token
                            Options.Remote.InvalidateCredentials();
                            return new StreamingSyncIterationResult { Retry = true };
                        }
                        TriggerCrudUpload();
                    }
                    else
                    {
                        logger.LogDebug("Sync complete");

                        if (targetCheckpoint == appliedCheckpoint)
                        {
                            UpdateSyncStatus(new SyncStatusOptions
                            {
                                Connected = true,
                                LastSyncedAt = DateTime.Now,
                            },
                            new UpdateSyncStatusOptions
                            {
                                ClearDownloadError = true
                            }
                            );
                        }
                        else if (validatedCheckpoint == targetCheckpoint)
                        {
                            var result = await Options.Adapter.SyncLocalDatabase(targetCheckpoint!);
                            if (!result.CheckpointValid)
                            {
                                // This means checksums failed. Start again with a new checkpoint.
                                // TODO: better back-off
                                await Task.Delay(50);
                                return new StreamingSyncIterationResult { Retry = false };
                            }
                            else if (!result.Ready)
                            {
                                // Checksums valid, but need more data for a consistent checkpoint.
                                // Continue waiting.
                            }
                            else
                            {
                                appliedCheckpoint = targetCheckpoint;
                                UpdateSyncStatus(new SyncStatusOptions
                                {
                                    Connected = true,
                                    LastSyncedAt = DateTime.Now,
                                    DataFlow = new SyncDataFlowStatus
                                    {
                                        Downloading = false,
                                    }
                                },
                                new UpdateSyncStatusOptions
                                {
                                    ClearDownloadError = true
                                });
                            }
                        }
                    }
                }

                logger.LogDebug("Stream input empty");
                // Connection closed. Likely due to auth issue.
                return new StreamingSyncIterationResult { Retry = true };
            }
        });

    }

    public new void Close()
    {
        crudUpdateCts?.Cancel();
        base.Close();
        crudUpdateCts = null;
    }

    public record ResponseData(
        [property: JsonProperty("write_checkpoint")] string WriteCheckpoint
    );

    public record ApiResponse(
        [property: JsonProperty("data")] ResponseData Data
    );
    public async Task<string> GetWriteCheckpoint()
    {
        var clientId = await Options.Adapter.GetClientId();
        var path = $"/write-checkpoint2.json?client_id={clientId}";
        var response = await Options.Remote.Get<ApiResponse>(path);

        return response.Data.WriteCheckpoint;
    }

    protected async Task InternalUploadAllCrud()
    {

        await locks.ObtainLock(new LockOptions<Task>
        {
            Type = LockType.CRUD,
            Callback = async () =>
            {
                CrudEntry? checkedCrudItem = null;

                while (true)
                {
                    UpdateSyncStatus(new SyncStatusOptions { DataFlow = new SyncDataFlowStatus { Uploading = true } });

                    try
                    {
                        // This is the first item in the FIFO CRUD queue.
                        var nextCrudItem = await Options.Adapter.NextCrudItem();
                        if (nextCrudItem != null)
                        {
                            if (checkedCrudItem?.ClientId == nextCrudItem.ClientId)
                            {
                                logger.LogWarning(
                                    "Potentially previously uploaded CRUD entries are still present in the upload queue. " +
                                    "Make sure to handle uploads and complete CRUD transactions or batches by calling and awaiting their `.Complete()` method. " +
                                    "The next upload iteration will be delayed."
                                );
                                throw new Exception("Delaying due to previously encountered CRUD item.");
                            }

                            checkedCrudItem = nextCrudItem;
                            await Options.UploadCrud();
                            UpdateSyncStatus(new SyncStatusOptions
                            {
                            },
                            new UpdateSyncStatusOptions
                            {
                                ClearUploadError = true
                            });

                        }
                        else
                        {
                            // Uploading is completed
                            await Options.Adapter.UpdateLocalTarget(GetWriteCheckpoint);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        checkedCrudItem = null;
                        UpdateSyncStatus(new SyncStatusOptions
                        {
                            DataFlow = new SyncDataFlowStatus
                            {
                                Uploading = false,
                                UploadError = ex
                            }
                        });

                        await DelayRetry();

                        if (!IsConnected)
                        {
                            // Exit loop if sync stream is no longer connected
                            break;
                        }

                        logger.LogDebug("Caught exception when uploading. Upload will retry after a delay. Exception: {message}", ex.Message);
                    }
                    finally
                    {
                        UpdateSyncStatus(new SyncStatusOptions { DataFlow = new SyncDataFlowStatus { Uploading = false } });
                    }
                }

                return Task.CompletedTask;
            }
        });
    }

    public async Task<bool> HasCompletedSync()
    {
        return await Options.Adapter.HasCompletedSync();
    }

    public async Task WaitForReady()
    {
        // Do nothing
        await Task.CompletedTask;
    }

    protected record UpdateSyncStatusOptions(
        bool? ClearDownloadError = null, bool? ClearUploadError = null
    );
    protected void UpdateSyncStatus(SyncStatusOptions options, UpdateSyncStatusOptions? updateOptions = null)
    {
        var updatedStatus = new SyncStatus(new SyncStatusOptions
        {
            Connected = options.Connected ?? SyncStatus.Connected,
            Connecting = !options.Connected.GetValueOrDefault() && (options.Connecting ?? SyncStatus.Connecting),
            LastSyncedAt = options.LastSyncedAt ?? SyncStatus.LastSyncedAt,
            DataFlow = new SyncDataFlowStatus
            {
                Uploading = options.DataFlow?.Uploading ?? SyncStatus.DataFlowStatus.Uploading,
                Downloading = options.DataFlow?.Downloading ?? SyncStatus.DataFlowStatus.Downloading,
                DownloadError = updateOptions?.ClearDownloadError == true ? null : options.DataFlow?.DownloadError ?? SyncStatus.DataFlowStatus.DownloadError,
                UploadError = updateOptions?.ClearUploadError == true ? null : options.DataFlow?.UploadError ?? SyncStatus.DataFlowStatus.UploadError,
            }
        });

        if (!SyncStatus.Equals(updatedStatus))
        {
            SyncStatus = updatedStatus;
            logger.LogDebug("[Sync status updated]: {message}", updatedStatus.ToJSON());
            // Only trigger this if there was a change
            Emit(new StreamingSyncImplementationEvent { StatusChanged = updatedStatus });
        }

        // Trigger this for all updates
        Emit(new StreamingSyncImplementationEvent { StatusUpdated = options });
    }

    private async Task DelayRetry()
    {
        if (Options.RetryDelayMs.HasValue)
        {
            await Task.Delay(Options.RetryDelayMs.Value);
        }
    }
}

enum LockType
{
    CRUD,
    SYNC
}

class LockOptions<T>
{
    public Func<Task<T>> Callback { get; set; } = null!;
    public LockType Type { get; set; }
    public CancellationToken? Token { get; set; }
}

class Lock
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async Task<T> Acquire<T>(Func<Task<T>> action)
    {
        await semaphore.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            semaphore.Release();
        }
    }
}

class StreamingSyncLocks
{
    protected Dictionary<LockType, Lock> Locks { get; private set; } = null!;

    public StreamingSyncLocks()
    {
        InitLocks();
    }

    private void InitLocks()
    {
        Locks = new Dictionary<LockType, Lock>
        {
            { LockType.CRUD, new Lock() },
            { LockType.SYNC, new Lock() }
        };
    }

    public async Task<T> ObtainLock<T>(LockOptions<T> lockOptions)
    {
        if (!Locks.TryGetValue(lockOptions.Type, out var lockInstance))
        {
            throw new InvalidOperationException($"Lock type {lockOptions.Type} not found");
        }

        return await lockInstance.Acquire(async () =>
        {
            if (lockOptions.Token?.IsCancellationRequested == true)
            {
                throw new OperationCanceledException("Aborted", lockOptions.Token.Value);
            }

            return await lockOptions.Callback();
        });
    }
}