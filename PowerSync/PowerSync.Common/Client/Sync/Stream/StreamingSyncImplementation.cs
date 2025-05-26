using System.Text;
using Newtonsoft.Json.Linq;

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
    private Action? notifyCompletedUploads;

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
            if (!SyncStatus.Connected)
            {
                return;
            }

            notifyCompletedUploads?.Invoke();

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
        _ = Task.Run(() =>
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

        // This loops runs until [retry] is false or the abort signal is set to aborted.
        // Aborting the nestedCts will:
        // - Abort any pending fetch requests
        // - Close any sync stream ReadableStreams (which will also close any established network requests)
        while (true)
        {
            UpdateSyncStatus(new SyncStatusOptions { Connecting = true });

            try
            {
                if (signal.Value.IsCancellationRequested)
                {
                    break;
                }
                Console.WriteLine("XXXX starting");
                await StreamingSyncIteration(nestedCts.Token, options);
                Console.WriteLine("XXXX ending");
                // Continue immediately
            }
            catch (Exception ex)
            {
                logger.LogError("Caught exception in streaming sync: {message}", ex.Message);
                Console.WriteLine(ex.StackTrace);
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

    protected async Task StreamingSyncIteration(CancellationToken signal,
        PowerSyncConnectionOptions? options)
    {
        await locks.ObtainLock(new LockOptions<bool>
        {
            Type = LockType.SYNC,
            Token = signal,
            Callback = async () =>
            {
                var resolvedOptions = new RequiredPowerSyncConnectionOptions
                {
                    Params = options?.Params ?? DEFAULT_STREAM_CONNECTION_OPTIONS.Params
                };

                await SyncIteration(signal, resolvedOptions);

                return true;
            }
        });
    }
    
    private async Task SyncIteration(CancellationToken? signal, RequiredPowerSyncConnectionOptions resolvedOptions)
    {
        Task? receivingLines = null;

        var nestedCts = new CancellationTokenSource();
        signal?.Register(() => { nestedCts.Cancel(); });

        async Task Connect(EstablishSyncStream instruction)
        {
            Console.WriteLine("----- We got het here again" + nestedCts.Token.IsCancellationRequested);
            Console.WriteLine("-----" + JsonConvert.SerializeObject(instruction.Request));
            var syncOptions = new SyncStreamOptions
            {
                Path = "/sync/stream",
                CancellationToken = nestedCts.Token,
                Data = instruction.Request
            };

            var stream = await Options.Remote.PostStreamRaw(syncOptions);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            
            syncOptions.CancellationToken.Register(() => {
                try { stream?.Close(); } catch { }
            });
            
            string? line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                logger.LogDebug("Parsing line for rust sync stream {message}", "xx");
                await Control("line_text", line);
            }
            Console.WriteLine("Done");
        }

        async Task Stop()
        {
            await Control("stop");
        }

        async Task Control(string op, object? payload = null)
        {
            logger.LogDebug("Control call {message}", op);

            var rawResponse = await Options.Adapter.Control(op, payload);
            HandleInstructions(Instruction.ParseInstructions(rawResponse));
        }

        void HandleInstructions(Instruction[] instructions)
        {
            foreach (var instruction in instructions)
            {
                HandleInstruction(instruction);
            }
        }

        void HandleInstruction(Instruction instruction)
        {
            switch (instruction)
            {
                case LogLine logLine:
                    switch (logLine.Severity)
                    {
                        case "DEBUG":
                            logger.LogDebug("{message}", logLine.Line);
                            break;
                        case "INFO":
                            logger.LogInformation("{message}", logLine.Line);
                            break;
                        case "WARNING":
                            logger.LogWarning("{message}", logLine.Line);
                            break;
                    }
                    break;
                case UpdateSyncStatus syncStatus:
                    var info = syncStatus.Status;
                    var coreCompleteSync =
                        info.PriorityStatus.FirstOrDefault(s => s.Priority == SyncProgress.FULL_SYNC_PRIORITY);
                    var completeSync = coreCompleteSync != null ? CoreStatusToSyncStatus(coreCompleteSync) : null;

                    UpdateSyncStatus(new SyncStatusOptions
                        {
                            Connected = info.Connected,
                            Connecting = info.Connecting,
                            LastSyncedAt = completeSync?.LastSyncedAt,
                            HasSynced = completeSync?.HasSynced,
                            PriorityStatusEntries = info.PriorityStatus.Select(CoreStatusToSyncStatus).ToArray(),
                            DataFlow = new SyncDataFlowStatus
                            {
                                Downloading = info.Downloading != null,
                                DownloadProgress = info.Downloading?.Buckets
                            }
                        },
                        new UpdateSyncStatusOptions
                        {
                            ClearDownloadError = true,
                        }
                    );
                    break;
                case EstablishSyncStream establishSyncStream:
                    if (receivingLines != null)
                    {
                        throw new Exception("Unexpected request to establish sync stream, already connected");
                    }
                    receivingLines = Connect(establishSyncStream);
                    break;
                case FetchCredentials fetchCredentials:
                    Options.Remote.InvalidateCredentials();
                    break;
                case CloseSyncStream:
                    nestedCts.Cancel();
                    logger.LogWarning("Closing stream");
                    break;
                case FlushFileSystem:
                    // ignore
                    break;
                case DidCompleteSync:
                    UpdateSyncStatus(
                        new SyncStatusOptions { },
                        new UpdateSyncStatusOptions { ClearDownloadError = true });
                    break;
            }
        }

        try
        {
            notifyCompletedUploads = () => { Task.Run(async () => await Control("completed_upload")); };
            logger.LogError("START");
            await Control("start", JsonConvert.SerializeObject(resolvedOptions.Params));
            if (receivingLines != null)
            {
                await receivingLines;
                logger.LogError("Done waiting");
            }
            else
            {
                Console.WriteLine("No receiving lines task was started, this should not happen.");
            }
        }
        finally
        {
            notifyCompletedUploads = null;
            await Stop();
        }
    }

    public new void Close()
    {
        crudUpdateCts?.Cancel();
        base.Close();
        crudUpdateCts = null;
    }

    public record ResponseData(
        [property: JsonProperty("write_checkpoint")]
        string WriteCheckpoint
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

                        logger.LogDebug(
                            "Caught exception when uploading. Upload will retry after a delay. Exception: {message}",
                            ex.Message);
                    }
                    finally
                    {
                        UpdateSyncStatus(new SyncStatusOptions
                            { DataFlow = new SyncDataFlowStatus { Uploading = false } });
                    }
                }

                return Task.CompletedTask;
            }
        });
    }

    public async Task WaitForReady()
    {
        // Do nothing
        await Task.CompletedTask;
    }

    protected record UpdateSyncStatusOptions(
        bool? ClearDownloadError = null,
        bool? ClearUploadError = null
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
                DownloadError = updateOptions?.ClearDownloadError == true
                    ? null
                    : options.DataFlow?.DownloadError ?? SyncStatus.DataFlowStatus.DownloadError,
                UploadError = updateOptions?.ClearUploadError == true
                    ? null
                    : options.DataFlow?.UploadError ?? SyncStatus.DataFlowStatus.UploadError,
                DownloadProgress = options.DataFlow?.DownloadProgress ?? SyncStatus.DataFlowStatus.DownloadProgress,
            },
            PriorityStatusEntries = options.PriorityStatusEntries ?? SyncStatus.PriorityStatusEntries
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

    private static DB.Crud.SyncPriorityStatus CoreStatusToSyncStatus(SyncPriorityStatus status)
    {
        return new DB.Crud.SyncPriorityStatus
        {
            Priority = status.Priority,
            HasSynced = status.HasSynced ?? null,
            LastSyncedAt = status?.LastSyncedAt != null ? new DateTime(status!.LastSyncedAt) : null
        };
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