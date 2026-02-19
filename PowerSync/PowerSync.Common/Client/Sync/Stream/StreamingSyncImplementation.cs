namespace PowerSync.Common.Client.Sync.Stream;

using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

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
        RetryDelayMs = 5000,
        Subscriptions = []
    };

    public new int RetryDelayMs { get; set; }

    public new int CrudUploadThrottleMs { get; set; }

    public SubscribedStream[] Subscriptions { get; init; } = null!;

}

public class StreamingSyncImplementationOptions : AdditionalConnectionOptions
{
    public IBucketStorageAdapter Adapter { get; init; } = null!;

    public SubscribedStream[] Subscriptions { get; init; } = null!;

    public Func<Task> UploadCrud { get; init; } = null!;

    public Remote Remote { get; init; } = null!;

    public ILogger? Logger { get; init; }
}

public class BaseConnectionOptions(Dictionary<string, object>? parameters = null, Dictionary<string, string>? appMetadata = null, bool? includeDefaultStreams = true)
{
    /// <summary>
    /// A set of metadata to be included in service logs.
    /// </summary>
    public Dictionary<string, string>? AppMetadata { get; set; } = appMetadata;

    /// <summary>
    /// These parameters are passed to the sync rules and will be available under the `user_parameters` object.
    /// </summary>
    public Dictionary<string, object>? Params { get; set; } = parameters;

    /// <summary>
    /// Whether to include streams that have `auto_subscribe: true` in their definition.
    /// 
    /// This defaults to `true`.
    /// </summary>
    public bool? IncludeDefaultStreams { get; set; } = includeDefaultStreams;
}

public class RequiredPowerSyncConnectionOptions : BaseConnectionOptions
{

    public new Dictionary<string, string> AppMetadata { get; set; } = new();

    public new Dictionary<string, object> Params { get; set; } = new();

    public new bool IncludeDefaultStreams { get; set; } = default;
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
    int? crudUploadThrottleMs = null,
    Dictionary<string, string>? appMetadata = null,
    bool? includeDefaultStreams = true
) : BaseConnectionOptions(@params, appMetadata, includeDefaultStreams)
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

public class SubscribedStream
{
    [JsonProperty("name")]
    public string Name { get; set; } = null!;

    [JsonProperty("params")]
    public Dictionary<string, object>? Params { get; set; }

}

public class StreamingSyncImplementation : EventStream<StreamingSyncImplementationEvent>
{
    public static RequiredPowerSyncConnectionOptions DEFAULT_STREAM_CONNECTION_OPTIONS = new()
    {
        AppMetadata = [],
        Params = [],
        IncludeDefaultStreams = true
    };

    public static readonly int DEFAULT_CRUD_UPLOAD_THROTTLE_MS = 1000;
    public static readonly int DEFAULT_RETRY_DELAY_MS = 5000;

    protected StreamingSyncImplementationOptions Options { get; }

    protected CancellationTokenSource? CancellationTokenSource { get; set; }

    private Task? streamingSyncTask;
    public Action TriggerCrudUpload { get; }
    private CancellationTokenSource? crudUpdateCts;

    private readonly ILogger logger;
    private SubscribedStream[] activeStreams;

    private bool isUploadingCrud;
    private Action? notifyCompletedUploads;
    private Action? handleActiveStreamsChange;

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
        activeStreams = options.Subscriptions;

        locks = new StreamingSyncLocks();
        logger = options.Logger ?? NullLogger.Instance;
        isUploadingCrud = false;

        CancellationTokenSource = null;

        TriggerCrudUpload = () =>
        {
            if (!SyncStatus.Connected || isUploadingCrud)
            {
                return;
            }

            isUploadingCrud = true;
            Task.Run(async () =>
            {
                await InternalUploadAllCrud();
                notifyCompletedUploads?.Invoke();
                isUploadingCrud = false;
            });
        };
    }

    /// <summary>
    /// Indicates if the sync service is connected.
    /// </summary>
    public bool IsConnected => SyncStatus.Connected;


    /// <summary>
    /// The timestamp of the last successful sync.
    /// </summary>
    public DateTime? LastSyncedAt => SyncStatus.LastSyncedAt;

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

        var tcs = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();

        var _ = Task.Run(() =>
        {
            foreach (var status in Listen(cts.Token))
            {
                if (status.StatusChanged != null)
                {
                    if (status.StatusChanged.Connected == false)
                    {
                        logger.LogWarning("Initial connect attempt did not successfully connect to server");
                    }

                    tcs.SetResult(true);
                    cts.Cancel();
                }
            }
        });

        streamingSyncTask = StreamingSync(CancellationTokenSource.Token, options);

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

        // This loops runs until [retry] is false or the abort signal is set to aborted.
        // Aborting the nestedCts will:
        // - Abort any pending fetch requests
        // - Close any sync stream ReadableStreams (which will also close any established network requests)
        while (true)
        {
            UpdateSyncStatus(new SyncStatusOptions { Connecting = true });
            var iterationResult = (StreamingSyncIterationResult?)null;
            var shouldDelayRetry = true;

            try
            {
                if (signal.Value.IsCancellationRequested)
                {
                    break;
                }
                iterationResult = await StreamingSyncIteration(nestedCts.Token, options);
            }
            catch (Exception ex)
            {
                var exMessage = ex.Message;
                if (ex.InnerException != null && (ex.InnerException is ObjectDisposedException || ex.InnerException is SocketException))
                {
                    exMessage = "Stream closed or timed out -" + ex.InnerException.Message;
                }

                // Either:
                //  - A network request failed with a failed connection or not OKAY response code.
                //  - There was a sync processing error.
                // This loop will retry.
                // The nested abort controller will cleanup any open network requests and streams.
                if (nestedCts.IsCancellationRequested)
                {
                    logger.LogWarning("Caught exception in streaming sync: {message}", exMessage);
                    shouldDelayRetry = false;
                }
                else
                {
                    logger.LogError("Caught exception in streaming sync: {message}", exMessage);
                }

                UpdateSyncStatus(new SyncStatusOptions
                {
                    Connected = false,
                    DataFlow = new SyncDataFlowStatus
                    {
                        DownloadError = ex
                    }
                });
            }
            finally
            {
                notifyCompletedUploads = null;

                if (!signal.Value.IsCancellationRequested)
                {
                    // Closing sync stream network requests before retry.
                    nestedCts.Cancel();
                    nestedCts = new CancellationTokenSource();
                }

                if (iterationResult != null && (iterationResult.ImmediateRestart != true && iterationResult.LegacyRetry != true))
                {

                    UpdateSyncStatus(new SyncStatusOptions
                    {
                        Connected = false,
                        Connecting = true
                    });

                    // On error, wait a little before retrying
                    if (shouldDelayRetry)
                    {
                        await DelayRetry();
                    }
                }
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
        public bool? LegacyRetry { get; init; }

        public bool? ImmediateRestart { get; init; }
    }

    protected record EnqueuedCommand
    {
        public string Command { get; init; } = null!;
        public object? Payload { get; init; }
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
                    AppMetadata = options?.AppMetadata ?? DEFAULT_STREAM_CONNECTION_OPTIONS.AppMetadata,
                    Params = options?.Params ?? DEFAULT_STREAM_CONNECTION_OPTIONS.Params,
                    IncludeDefaultStreams = options?.IncludeDefaultStreams ?? DEFAULT_STREAM_CONNECTION_OPTIONS.IncludeDefaultStreams,
                };

                return await RustStreamingSyncIteration(signal, resolvedOptions);
            }
        });
    }


    protected async Task<StreamingSyncIterationResult> RustStreamingSyncIteration(CancellationToken? signal, RequiredPowerSyncConnectionOptions resolvedOptions)
    {
        Task? receivingLines = null;
        bool hadSyncLine = false;
        bool hideDisconnectOnRestart = false;

        var controlInvocations = (EventStream<EnqueuedCommand>)null!;

        var nestedCts = new CancellationTokenSource();
        signal?.Register(() => { nestedCts.Cancel(); });

        async Task Connect(EstablishSyncStream instruction)
        {
            var syncOptions = new SyncStreamOptions
            {
                Path = "/sync/stream",
                CancellationToken = nestedCts.Token,
                Data = instruction.Request
            };

            controlInvocations = new EventStream<EnqueuedCommand>();
            try
            {
                _ = Task.Run(async () =>
                {
                    await foreach (var line in controlInvocations.ListenAsync(new CancellationToken()))
                    {
                        await Control(line.Command, line.Payload);

                        // Triggers a local CRUD upload when the first sync line has been received.
                        // This allows uploading local changes that have been made while offline or disconnected.
                        if (!hadSyncLine)
                        {
                            TriggerCrudUpload();
                            hadSyncLine = true;
                        }
                    }
                });

                var stream = await Options.Remote.PostStreamRaw(syncOptions);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                syncOptions.CancellationToken.Register(() =>
                {
                    try { stream?.Close(); } catch { }
                });

                UpdateSyncStatus(new SyncStatusOptions
                {
                    Connected = true
                });

                // Read lines in a cancellation-aware manner.
                // ReadLineAsync() doesn't support CancellationToken on all .NET versions,
                // so we use WhenAny to check for cancellation between reads.
                while (!syncOptions.CancellationToken.IsCancellationRequested)
                {
                    var readTask = reader.ReadLineAsync();

                    // Create a task that completes when cancellation is requested
                    var cancellationTcs = new TaskCompletionSource<bool>();
                    using var registration = syncOptions.CancellationToken.Register(() => cancellationTcs.TrySetResult(true));

                    var completedTask = await Task.WhenAny(readTask, cancellationTcs.Task);

                    if (completedTask == cancellationTcs.Task)
                    {
                        // Cancellation was requested, exit the loop
                        break;
                    }

                    var line = await readTask;
                    if (line == null)
                    {
                        // Stream ended
                        break;
                    }

                    controlInvocations?.Emit(new EnqueuedCommand
                    {
                        Command = PowerSyncControlCommand.PROCESS_TEXT_LINE,
                        Payload = line
                    });
                }
            }
            finally
            {
                var activeInstructions = controlInvocations;
                controlInvocations = null;
                activeInstructions?.Close();
            }
        }

        async Task Stop()
        {
            await Control(PowerSyncControlCommand.STOP);
        }

        async Task Control(string op, object? payload = null)
        {
            var rawResponse = await Options.Adapter.Control(op, payload);
            logger.LogTrace("powersync_control {op}, {payload}, {rawResponse}", op, payload, rawResponse);
            HandleInstructions(Instruction.ParseInstructions(rawResponse));
        }

        async void HandleInstructions(Instruction[] instructions)
        {
            foreach (var instruction in instructions)
            {
                await HandleInstruction(instruction);
            }
        }

        async Task HandleInstruction(Instruction instruction)
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
                    UpdateSyncStatus(CoreInstructionHelpers.CoreStatusToSyncStatusOptions(syncStatus.Status));
                    break;
                case EstablishSyncStream establishSyncStream:
                    if (receivingLines != null)
                    {
                        throw new Exception("Unexpected request to establish sync stream, already connected");
                    }

                    receivingLines = Connect(establishSyncStream);
                    break;
                case FetchCredentials fetchCredentials:
                    if (fetchCredentials.DidExpire)
                    {
                        Options.Remote.InvalidateCredentials();
                    }
                    else
                    {
                        Options.Remote.InvalidateCredentials();

                        // Restart iteration after the credentials have been refreshed.
                        try
                        {
                            await Options.Remote.FetchCredentials();
                            controlInvocations?.Emit(new EnqueuedCommand
                            {
                                Command = PowerSyncControlCommand.NOTIFY_TOKEN_REFRESHED
                            });
                        }
                        catch (Exception err)
                        {
                            logger.LogWarning("Could not prefetch credentials: {message}", err.Message);
                        }

                    }
                    break;
                case CloseSyncStream closeSyncStream:
                    nestedCts.Cancel();
                    hideDisconnectOnRestart = closeSyncStream.HideDisconnect;
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
            var options = new
            {
                parameters = resolvedOptions.Params,
                active_streams = activeStreams,
                include_defaults = resolvedOptions.IncludeDefaultStreams,
                app_metadata = resolvedOptions.AppMetadata
            };
            await Control(PowerSyncControlCommand.START, JsonConvert.SerializeObject(options));

            notifyCompletedUploads = () =>
            {
                Task.Run(() =>
                {
                    if (controlInvocations != null && !controlInvocations.Closed)
                    {
                        controlInvocations?.Emit(new EnqueuedCommand
                        {
                            Command = PowerSyncControlCommand.NOTIFY_CRUD_UPLOAD_COMPLETED
                        });
                    }
                });
            };

            handleActiveStreamsChange = () =>
            {
                if (controlInvocations != null && !controlInvocations.Closed)
                {
                    controlInvocations?.Emit(new EnqueuedCommand
                    {
                        Command = PowerSyncControlCommand.UPDATE_SUBSCRIPTIONS,
                        Payload = JsonConvert.SerializeObject(activeStreams)
                    });
                }
            };

            if (receivingLines != null)
            {
                await receivingLines;
            }
        }
        finally
        {
            notifyCompletedUploads = null;
            handleActiveStreamsChange = null;
            await Stop();
        }

        return new StreamingSyncIterationResult { ImmediateRestart = hideDisconnectOnRestart };
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
        try
        {
            var updatedStatus = new SyncStatus(new SyncStatusOptions
            {
                Connected = options.Connected ?? SyncStatus.Connected,
                Connecting = !options.Connected.GetValueOrDefault() && (options.Connecting ?? SyncStatus.Connecting),
                LastSyncedAt = options.LastSyncedAt ?? SyncStatus.LastSyncedAt,
                PriorityStatusEntries = options.PriorityStatusEntries ?? SyncStatus.PriorityStatusEntries,
                DataFlow = new SyncDataFlowStatus
                {
                    Uploading = options.DataFlow?.Uploading ?? SyncStatus.DataFlowStatus.Uploading,
                    Downloading = options.DataFlow?.Downloading ?? SyncStatus.DataFlowStatus.Downloading,
                    DownloadProgress = options.DataFlow?.DownloadProgress ?? SyncStatus.DataFlowStatus.DownloadProgress,
                    DownloadError = updateOptions?.ClearDownloadError == true ? null : options.DataFlow?.DownloadError ?? SyncStatus.DataFlowStatus.DownloadError,
                    UploadError = updateOptions?.ClearUploadError == true ? null : options.DataFlow?.UploadError ?? SyncStatus.DataFlowStatus.UploadError,
                    InternalStreamSubscriptions = options.DataFlow?.InternalStreamSubscriptions ?? SyncStatus.DataFlowStatus.InternalStreamSubscriptions
                }
            });

            if (!SyncStatus.Equals(updatedStatus))
            {
                SyncStatus = updatedStatus;
                logger.LogDebug("[Sync status changed]: {message}", updatedStatus.ToJSON());
                // Only trigger this if there was a change
                Emit(new StreamingSyncImplementationEvent { StatusChanged = updatedStatus });
            }

            // Trigger this for all updates
            Emit(new StreamingSyncImplementationEvent { StatusUpdated = options });
        }
        catch (Exception ex)
        {
            logger.LogError("Error updating sync status: {message}", ex.Message);
        }
    }

    private async Task DelayRetry()
    {
        if (Options.RetryDelayMs.HasValue)
        {
            await Task.Delay(Options.RetryDelayMs.Value);
        }
    }


    public void UpdateSubscriptions(SubscribedStream[] subscriptions)
    {
        activeStreams = subscriptions;
        handleActiveStreamsChange?.Invoke();
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
