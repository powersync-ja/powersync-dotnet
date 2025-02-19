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

public class RequiredPowerSyncConnectionOptions : BaseConnectionOptions
{
    public new Dictionary<string, object> Params { get; set; } = new();
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
        Console.WriteLine($"[DEBUG] {DateTime.Now:O} - {message}");
    }

    public void Warn(string message)
    {
        Console.WriteLine($"[WARN] {DateTime.Now:O} - {message}");
    }

    public void Error(string message)
    {
        Console.WriteLine($"[ERROR] {DateTime.Now:O} - {message}");
    }
}


public class StreamingSyncImplementation : IStreamingSyncImplementation
{
    public RequiredPowerSyncConnectionOptions DEFAULT_STREAM_CONNECTION_OPTIONS = new()
    {
        Params = []
    };

    public static readonly int DEFAULT_CRUD_UPLOAD_THROTTLE_MS = 1000;
    public static readonly int DEFAULT_RETRY_DELAY_MS = 5000;

    protected StreamingSyncImplementationOptions Options { get; }

    protected CancellationTokenSource? CancellationTokenSource { get; set; }

    private Task? streamingSyncTask;
    public Action TriggerCrudUpload { get; }

    private readonly Logger logger = new Logger();

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

        CancellationTokenSource = null;

        // TODO CL throttling
        TriggerCrudUpload = () =>
        {
            if (!SyncStatus.Connected || SyncStatus.DataFlowStatus.Uploading)
            {
                return;
            }
            // Offloads the work to the ThreadPool, ensuring it runs on a background thread
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

    protected async Task StreamingSync(CancellationToken? signal, PowerSyncConnectionOptions? options)
    {
        if (signal == null)
        {
            CancellationTokenSource = new CancellationTokenSource();
            signal = CancellationTokenSource.Token;
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
                logger.Error($"Caught exception in streaming sync: {ex.Message}");
                /**
                * Either:
                *  - A network request failed with a failed connection or not OKAY response code.
                *  - There was a sync processing error.
                * This loop will retry.
                * The nested abort controller will cleanup any open network requests and streams.
                * The WebRemote should only abort pending fetch requests or close active Readable streams.
                */
                // On error, wait a little before retrying
                await DelayRetry();
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

    protected record StreamingSyncIterationResult
    {
        public bool Retry { get; init; }
    }
    // protected async streamingSyncIteration
    protected async Task<StreamingSyncIterationResult> StreamingSyncIteration(CancellationToken signal, PowerSyncConnectionOptions? options)
    {
        var resolvedOptions = new RequiredPowerSyncConnectionOptions
        {
            Params = options?.Params ?? DEFAULT_STREAM_CONNECTION_OPTIONS.Params
        };

        logger.Debug("Streaming sync iteration started");
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

        logger.Debug("Requesting stream from server");

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
                logger.Debug("Stream established. Processing events");
            }

            Console.WriteLine("line:" + line.GetType().Name + JsonConvert.SerializeObject(line, Formatting.Indented));

            if (line == null)
            {
                logger.Debug("Stream has closed while waiting");
                // The stream has closed while waiting
                return new StreamingSyncIterationResult { Retry = true };
            }

            // // A connection is active and messages are being received
            if (!SyncStatus.Connected)
            {
                logger.Debug("TO BE CONNECTED NOW");
                // There is a connection now
                UpdateSyncStatus(new SyncStatusOptions
                {
                    Connected = true
                });
                TriggerCrudUpload();
            }

            if (line is StreamingSyncCheckpoint syncCheckpoint)
            {
                logger.Debug("Sync checkpoint: " + syncCheckpoint);

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
                    logger.Debug("Removing buckets: " + string.Join(", ", bucketsToDelete));
                }

                bucketSet = newBuckets;
                await Options.Adapter.RemoveBuckets([.. bucketsToDelete]);
                await Options.Adapter.SetTargetCheckpoint(targetCheckpoint);
            }
            else if (line is StreamingSyncCheckpointComplete checkpointComplete)
            {
                logger.Debug("Checkpoint complete: " + targetCheckpoint);
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
                    logger.Debug("Validated checkpoint: " + appliedCheckpoint);

                    UpdateSyncStatus(new SyncStatusOptions
                    {
                        Connected = true,
                        LastSyncedAt = DateTime.Now,
                        DataFlow = new SyncDataFlowStatus { Downloading = false }
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

                var newCheckpoint = new Checkpoint
                {
                    LastOpId = diff.LastOpId,
                    Buckets = [.. newBuckets.Values],
                    WriteCheckpoint = diff.WriteCheckpoint
                };

                targetCheckpoint = newCheckpoint;

                bucketSet = [.. newBuckets.Keys];

                var bucketsToDelete = diff.RemovedBuckets.ToArray();
                if (bucketsToDelete.Length > 0)
                {
                    logger.Debug("Remove buckets: " + string.Join(", ", bucketsToDelete));
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
                    logger.Debug("Token expiring; reconnect");

                    // For a rare case where the backend connector does not update the token
                    // (uses the same one), this should have some delay.
                    //
                    await DelayRetry();
                    return new StreamingSyncIterationResult { Retry = true };
                }
                TriggerCrudUpload();
            }
            else
            {
                logger.Debug("Sync complete");

                if (targetCheckpoint == appliedCheckpoint)
                {
                    UpdateSyncStatus(new SyncStatusOptions
                    {
                        Connected = true,
                        LastSyncedAt = DateTime.Now
                    });
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
                                Downloading = false
                            }
                        });
                    }
                }
            }
        }

        logger.Debug("Stream input empty");
        // Connection closed. Likely due to auth issue.
        return new StreamingSyncIterationResult { Retry = true };
    }

    public void Dispose()
    {
        throw new NotImplementedException();
        // this.crudUpdateListener?.();
        // this.crudUpdateListener = undefined;
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

    // TODO CL Locking
    protected async Task InternalUploadAllCrud()
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
                        logger.Warn(
                            "Potentially previously uploaded CRUD entries are still present in the upload queue. " +
                            "Make sure to handle uploads and complete CRUD transactions or batches by calling and awaiting their `.Complete()` method. " +
                            "The next upload iteration will be delayed."
                        );
                        throw new Exception("Delaying due to previously encountered CRUD item.");
                    }

                    checkedCrudItem = nextCrudItem;
                    await Options.UploadCrud();
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
                UpdateSyncStatus(new SyncStatusOptions { DataFlow = new SyncDataFlowStatus { Uploading = false } });

                await DelayRetry();

                if (!IsConnected)
                {
                    // Exit loop if sync stream is no longer connected
                    break;
                }

                logger.Debug($"Caught exception when uploading. Upload will retry after a delay. Exception: {ex.Message}");
            }
            finally
            {
                UpdateSyncStatus(new SyncStatusOptions { DataFlow = new SyncDataFlowStatus { Uploading = false } });
            }
        }
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

    public Task WaitForStatus(SyncStatusOptions status)
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
                Uploading = options.DataFlow?.Uploading ?? SyncStatus.DataFlowStatus.Uploading,
                Downloading = options.DataFlow?.Downloading ?? SyncStatus.DataFlowStatus.Downloading
            }
        });

        if (!SyncStatus.Equals(updatedStatus))
        {
            SyncStatus = updatedStatus;
            Console.WriteLine($"[Sync status updated]: {updatedStatus.ToJSON()}");
            // Only trigger this if there was a change
            // IterateListeners(cb => cb.StatusChanged?.Invoke(updatedStatus));
        }

        // Trigger this for all updates
        // IterateListeners(cb => cb.StatusUpdated?.Invoke(options));
    }

    private async Task DelayRetry()
    {
        if (Options.RetryDelayMs.HasValue)
        {
            await Task.Delay(Options.RetryDelayMs.Value);
        }
    }
}