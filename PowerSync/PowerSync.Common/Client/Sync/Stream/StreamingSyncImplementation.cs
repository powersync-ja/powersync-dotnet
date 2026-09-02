namespace PowerSync.Common.Client.Sync.Stream;

using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Newtonsoft.Json;

using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.DB.Crud;
using PowerSync.Common.Utils;

public class AdditionalConnectionOptions(int? retryDelayMs = null, int? crudUploadThrottleMs = null)
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

    /// <summary>
    /// Posts a checkpoint request with the connector. Null when the connector doesn't support that,
    /// in which case the request is posted to the PowerSync service directly.
    /// </summary>
    public Func<string, string, Task<string>>? PostCheckpointRequest { get; init; }

    public Remote Remote { get; init; } = null!;

    public ILogger? Logger { get; init; }

    /// <summary>
    /// Source of the delays in the sync loops. Tests substitute a fake clock so they don't have to
    /// wait out real retry delays.
    /// </summary>
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

public class BaseConnectionOptions(Dictionary<string, object>? parameters = null, Dictionary<string, string>? appMetadata = null, bool? includeDefaultStreams = true, CheckpointMode? checkpointMode = null)
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

    /// <summary>
    /// The mode used to request checkpoint requests from the PowerSync service.
    /// 
    /// Defaults to <see cref="CheckpointMode.Legacy" />, but will default to <see cref="CheckpointMode.Requests" /> in a future release.
    /// </summary>
    public CheckpointMode CheckpointMode { get; set; } = checkpointMode ?? CheckpointMode.Legacy;
}

public class RequiredPowerSyncConnectionOptions : BaseConnectionOptions
{
    public new Dictionary<string, string> AppMetadata { get; set; } = new();

    public new Dictionary<string, object> Params { get; set; } = new();

    public new bool IncludeDefaultStreams { get; set; } = default;
}

public class StreamingSyncImplementationEvents : EventManager
{
    public interface IStreamingSyncImplementationEvent;

    public class StatusUpdatedEvent(SyncStatus status) : IStreamingSyncImplementationEvent
    {
        public SyncStatus Status { get; set; } = status;
    }
    public class StatusChangedEvent(SyncStatus status) : IStreamingSyncImplementationEvent
    {
        public SyncStatus Status { get; set; } = status;
    }

    /// <summary>
    /// See whenever a status update has been attempted to be made or refreshed.
    /// </summary>
    public EventStream<StatusUpdatedEvent> OnStatusUpdated { get; } = new();

    /// <summary>
    /// See whenever the status' members have changed in value.
    /// </summary>
    public EventStream<StatusChangedEvent> OnStatusChanged { get; } = new();

    public StreamingSyncImplementationEvents()
    {
        Register(OnStatusUpdated);
        Register(OnStatusChanged);
    }
}

public class PowerSyncConnectionOptions(
    Dictionary<string, object>? @params = null,
    int? retryDelayMs = null,
    int? crudUploadThrottleMs = null,
    Dictionary<string, string>? appMetadata = null,
    bool? includeDefaultStreams = true,
    CheckpointMode? checkpointMode = null
) : BaseConnectionOptions(@params, appMetadata, includeDefaultStreams, checkpointMode)
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

public class StreamingSyncImplementation : ICloseable
{
    public static readonly RequiredPowerSyncConnectionOptions DEFAULT_STREAM_CONNECTION_OPTIONS = new()
    {
        AppMetadata = [],
        Params = [],
        IncludeDefaultStreams = true,
        CheckpointMode = CheckpointMode.Legacy,
    };

    public StreamingSyncImplementationEvents Events { get; } = new();

    public static readonly int DEFAULT_CRUD_UPLOAD_THROTTLE_MS = 1000;
    public static readonly int DEFAULT_RETRY_DELAY_MS = 5000;

    protected StreamingSyncImplementationOptions Options { get; }

    protected CancellationTokenSource? CancellationTokenSource { get; set; }

    private Task? streamingSyncTask;

    private CancellationTokenSource? crudUpdateCts;
    private Task? crudUpdateTask;

    private readonly CheckpointStateSignals checkpointState = new();

    /// <summary>
    /// The highest checkpoint request id the core extension has reported as applied, if any.
    /// </summary>
    private string? lastAppliedCheckpointRequestId;

    private readonly ILogger logger;
    private SubscribedStream[] activeStreams;

    private Action? notifyCompletedUploads;
    private Action? handleActiveStreamsChange;

    /// <summary>Signals <see cref="CrudUploadLoop"/> that there may be local writes to upload.</summary>
    private readonly Channel<bool> crudUploadRequested = CreateNotifier<bool>();

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

        CancellationTokenSource = null;
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

        // Subscribe to events before starting StreamingSync to not miss the Connected == true event
        var listener = Events.OnStatusChanged.ListenAsync(cts.Token);

        streamingSyncTask = StreamingSync(CancellationTokenSource.Token, options);

        var _ = Task.Run(async () =>
        {
            await foreach (var status in listener)
            {
                if (status.Status.Connected == true)
                {
                    tcs.TrySetResult(true);
                    cts.Cancel();
                    return;
                }
            }

            // Connection closed prematurely
            logger.LogWarning("Initial connect attempt did not successfully connect to server");
            tcs.TrySetResult(true);
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
        catch (OperationCanceledException)
        {
            // Expected: disconnecting cancels whatever the sync loops had in flight.
        }
        catch (Exception ex)
        {
            // The operation might have failed, all we care about is if it has completed
            logger.LogWarning("Streaming sync task failed during disconnect: {Message}", ex.Message);
        }
        streamingSyncTask = null;
        CancellationTokenSource = null;

        UpdateSyncStatus(new SyncStatusOptions { Connected = false, Connecting = false });
    }

    /// <summary>
    /// Requests a CRUD upload, without waiting for it to complete.
    /// </summary>
    public void TriggerCrudUpload()
    {
        crudUploadRequested.Writer.TryWrite(true);
    }

    /// <summary>
    /// Allocates the next checkpoint request id and posts it, waiting for the active download
    /// iteration to have reconciled checkpoint state with the service first.
    /// </summary>
    private async Task<string> RequestNextCheckpointFromService(CancellationToken signal)
    {
        await checkpointState.WaitForCheckpointRequestsReady(signal);

        var nextCheckpointRequestId = await Options.Adapter.NextCheckpointRequestId()
            ?? throw new InvalidOperationException("The core extension did not return a checkpoint request id.");
        var clientId = await Options.Adapter.GetClientId();
        return await RequestCheckpointFromService(signal, new CheckpointRequestPayload
        {
            ClientId = clientId,
            CheckpointRequestId = nextCheckpointRequestId,
        });
    }

    private async Task<string> RequestCheckpointFromService(CancellationToken signal, CheckpointRequestPayload request)
    {
        // First, check if we can use a custom checkpoint request implementation.
        if (Options.PostCheckpointRequest != null)
        {
            return await Options.PostCheckpointRequest(request.ClientId, request.CheckpointRequestId);
        }

        var status = await Options.Remote.FetchJson<CheckpointRequestResponse>(
            path: "/sync/checkpoint-request",
            method: HttpMethod.Post,
            data: request,
            ct: signal
        );
        return status.Data.CheckpointRequestId;
    }

    /// <summary>
    /// Asks the service for the checkpoint request state it has for this client, and hands it to the
    /// core extension so that subsequent requests continue from a counter both parties agree on.
    /// </summary>
    private async Task SeedCheckpointRequestState(CancellationToken signal, CheckpointRequestPayload request)
    {
        var seed = await RequestCheckpointFromService(signal, request);
        await Options.Adapter.SeedCheckpointRequestId(seed);
    }

    // TODO convert write checkpoint data type to long in a future release
    private async Task<string> GetLegacyWriteCheckpoint()
    {
        var clientId = await Options.Adapter.GetClientId();
        var path = $"/write-checkpoint2.json?client_id={clientId}";
        var response = await Options.Remote.FetchJson<LegacyWriteCheckpointApiResponse>(path);

        logger.LogDebug("Created write checkpoint: {checkpoint}", response.Data.WriteCheckpoint);
        return response.Data.WriteCheckpoint;
    }

    protected async Task StreamingSync(CancellationToken? signal, PowerSyncConnectionOptions? options)
    {
        if (signal == null)
        {
            CancellationTokenSource = new CancellationTokenSource();
            signal = CancellationTokenSource.Token;
        }

        var token = signal.Value;
        var resolvedOptions = options ?? new PowerSyncConnectionOptions();

        try
        {
            await Task.WhenAll(
                DownloadLoop(token, resolvedOptions),
                CrudUploadLoop(token, resolvedOptions),
                RepostUnacknowledgedCheckpointRequests(token, resolvedOptions)
            );
        }
        finally
        {
            // These loops only complete when we want to disconnect. No further sync iteration can
            // resume checkpoint requests, so fail any that are still pending.
            checkpointState.Disconnected();
        }
    }

    protected async Task DownloadLoop(CancellationToken signal, PowerSyncConnectionOptions options)
    {
        var retryDelayMs = options.RetryDelayMs ?? DEFAULT_RETRY_DELAY_MS;

        crudUpdateCts = new CancellationTokenSource();
        crudUpdateTask = Task.Run(async () =>
        {
            await foreach (var _ in Options.Adapter.Events.OnCrudUpdate.ListenAsync(crudUpdateCts.Token))
            {
                TriggerCrudUpload();
            }
        });

        // Create a new cancellation token source for nested operations.
        // This is needed to close any previous connections.
        var nestedCts = new CancellationTokenSource();
        signal.Register(() =>
        {
            nestedCts.Cancel();
            crudUpdateCts?.Cancel();
            crudUpdateCts = null;
            try { crudUpdateTask?.Wait(2000); } catch (Exception) { }
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
                if (signal.IsCancellationRequested)
                {
                    break;
                }
                iterationResult = await StreamingSyncIteration(nestedCts.Token, options);

                if (iterationResult.ImmediateRestart == true || iterationResult.LegacyRetry == true)
                {
                    shouldDelayRetry = false;
                }
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

                if (!signal.IsCancellationRequested)
                {
                    // Closing sync stream network requests before retry.
                    nestedCts.Cancel();
                    nestedCts = new CancellationTokenSource();
                }

                if (shouldDelayRetry)
                {
                    UpdateSyncStatus(new SyncStatusOptions
                    {
                        Connected = false,
                        Connecting = true
                    });

                    // Someone wanting to request a checkpoint needs a seeded iteration, so cut the
                    // delay short instead of making them wait for it.
                    await DelayRetry(signal, retryDelayMs, resumeOnCheckpointRequest: true);
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

    /// <summary>
    /// Uploads local writes for as long as the connection lasts: once on connect, and then whenever
    /// <see cref="TriggerCrudUpload"/> signals that there may be more.
    /// </summary>
    protected async Task CrudUploadLoop(CancellationToken signal, PowerSyncConnectionOptions options)
    {
        var throttleMs = options.CrudUploadThrottleMs ?? DEFAULT_CRUD_UPLOAD_THROTTLE_MS;

        try
        {
            while (!signal.IsCancellationRequested)
            {
                // Start the initial CRUD upload on connect. Then, keep polling until we're done.
                await Task.WhenAll(
                    InternalUploadAllCrud(signal, options),
                    DelayRetry(signal, throttleMs)
                );

                await crudUploadRequested.Reader.ReadAsync(signal);
            }
        }
        catch (OperationCanceledException) when (signal.IsCancellationRequested)
        {
            // Disconnecting.
        }
        catch (Exception ex)
        {
            logger.LogError("Error in CRUD upload loop: {message}", ex.Message);
        }
    }

    /// <summary>
    /// Periodically re-posts the current checkpoint request while the service has not applied it yet.
    /// <para />
    /// The service is allowed to forget checkpoint requests, and re-posting an id it has already seen
    /// is a cheap no-op, so this doubles as a catch-all for requests lost to network failures.
    /// </summary>
    protected async Task RepostUnacknowledgedCheckpointRequests(CancellationToken signal, PowerSyncConnectionOptions options)
    {
        if (options.CheckpointMode is not CheckpointMode.Requests requests)
        {
            return;
        }

        var retryDelayMs = (int)requests.RetryDelayMs;

        while (!signal.IsCancellationRequested)
        {
            try
            {
                // Never wakes the download loop: this only re-posts what another caller requested.
                await checkpointState.WaitForCheckpointRequestsReady(signal, wakeDownloadLoop: false);

                var requestId = await Options.Adapter.CurrentCheckpointRequestId();

                // Give the request some time to sync.
                await DelayRetry(signal, retryDelayMs);

                // If a new request was made, reset the timer.
                if (requestId != await Options.Adapter.CurrentCheckpointRequestId())
                {
                    continue;
                }

                // If the request was applied, we don't need to retry.
                if (requestId == null || IsCheckpointRequestApplied(requestId))
                {
                    continue;
                }

                // Make sure we're online and ready before making the request.
                await checkpointState.WaitForCheckpointRequestsReady(signal, wakeDownloadLoop: false);

                // It's safe if this request races with a new one, the service will reject it.
                logger.LogDebug("Retry checkpoint request {requestId}", requestId);
                await RequestCheckpointFromService(signal, new CheckpointRequestPayload
                {
                    ClientId = await Options.Adapter.GetClientId(),
                    CheckpointRequestId = requestId,
                });
            }
            catch (OperationCanceledException) when (signal.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Error retrying checkpoint request: {message}", ex.Message);

                try
                {
                    await DelayRetry(signal, retryDelayMs);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Whether the core extension has reported <paramref name="requestId"/> (or a later request) as
    /// applied.
    /// </summary>
    private bool IsCheckpointRequestApplied(string requestId)
    {
        return lastAppliedCheckpointRequestId is { } applied
            && long.TryParse(applied, out var appliedId)
            && long.TryParse(requestId, out var required)
            && appliedId >= required;
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

        /// <summary>
        /// Set instead of <see cref="Command"/> when work running alongside the iteration (seeding
        /// checkpoint state) failed and the iteration should fail with it.
        /// </summary>
        public Exception? Error { get; init; }
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
                    CheckpointMode = options?.CheckpointMode ?? DEFAULT_STREAM_CONNECTION_OPTIONS.CheckpointMode,
                };

                return await RustStreamingSyncIteration(signal, resolvedOptions);
            }
        });
    }


    protected async Task<StreamingSyncIterationResult> RustStreamingSyncIteration(CancellationToken? signal, RequiredPowerSyncConnectionOptions resolvedOptions)
    {
        bool hideDisconnectOnRestart = false;
        Action? notifyTokenRefreshed = null;

        // A failure opening or reading the stream, surfaced from the control loop so it retries.
        Exception? streamError = null;

        // Reconciling checkpoint request state runs alongside line processing rather than blocking it.
        Task? seedingCheckpointState = null;

        var nestedCts = new CancellationTokenSource();
        signal?.Register(() => { nestedCts.Cancel(); });

        async Task ReceiveSyncLines(StreamingSyncRequest request, EventStream<EnqueuedCommand> sink, CancellationToken token)
        {
            var syncOptions = new SyncStreamOptions
            {
                Path = "/sync/stream",
                CancellationToken = token,
                Data = request
            };

            var established = false;
            try
            {
                var stream = await Options.Remote.PostStreamRaw(syncOptions);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                token.Register(() =>
                {
                    try { stream?.Close(); } catch { }
                });

                // We're connected here, tell core extension
                sink.Emit(new EnqueuedCommand
                {
                    Command = PowerSyncControlCommand.CONNECTION_STATE,
                    Payload = PowerSyncControlConnectionState.ESTABLISHED
                });
                established = true;

                // Read lines in a cancellation-aware manner.
                // ReadLineAsync() doesn't support CancellationToken on all .NET versions,
                // so we use WhenAny to check for cancellation between reads.
                while (!token.IsCancellationRequested)
                {
                    var readTask = reader.ReadLineAsync();

                    // Create a task that completes when cancellation is requested
                    var cancellationTcs = new TaskCompletionSource<bool>();
                    using var registration = token.Register(() => cancellationTcs.TrySetResult(true));

                    var completedTask = await Task.WhenAny(readTask, cancellationTcs.Task);

                    if (completedTask == cancellationTcs.Task)
                    {
                        // Cancellation was requested, exit the loop. The read is still in
                        // flight and faults once the stream is closed during teardown, so
                        // observe it to keep that failure out of
                        // TaskScheduler.UnobservedTaskException.
                        _ = readTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        break;
                    }

                    var line = await readTask;
                    if (line == null)
                    {
                        // Stream ended
                        break;
                    }

                    sink.Emit(new EnqueuedCommand
                    {
                        Command = PowerSyncControlCommand.PROCESS_TEXT_LINE,
                        Payload = line
                    });
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    streamError = ex;
                }
            }
            finally
            {
                if (established && !sink.Closed)
                {
                    sink.Emit(new EnqueuedCommand
                    {
                        Command = PowerSyncControlCommand.CONNECTION_STATE,
                        Payload = PowerSyncControlConnectionState.END
                    });
                }

                sink.Close();
            }
        }

        async Task Stop()
        {
            foreach (var instruction in await InvokePowerSyncControl(PowerSyncControlCommand.STOP))
            {
                // Unconditionally ending the iteration, so interrupting instructions don't apply.
                if (instruction is NonInterruptingInstruction nonInterrupting)
                {
                    await HandleInstruction(nonInterrupting);
                }
            }
        }

        async Task<Instruction[]> InvokePowerSyncControl(string op, object? payload = null)
        {
            var rawResponse = await Options.Adapter.Control(op, payload);
            logger.LogTrace("powersync_control {op}, {payload}, {rawResponse}", op, payload, rawResponse);
            return Instruction.ParseInstructions(rawResponse);
        }

        async Task HandleInstruction(NonInterruptingInstruction instruction)
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
                    lastAppliedCheckpointRequestId = syncStatus.Status.LastAppliedCheckpointRequestId;
                    UpdateSyncStatus(CoreInstructionHelpers.CoreStatusToSyncStatusOptions(syncStatus.Status));
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
                            notifyTokenRefreshed?.Invoke();
                        }
                        catch (Exception err)
                        {
                            logger.LogWarning("Could not prefetch credentials: {message}", err.Message);
                        }

                    }
                    break;
                case UnknownSyncInstruction unknown:
                    logger.LogWarning("Unknown instruction from the core extension: {source}", unknown.Source);
                    break;
                case DidCompleteSync:
                    UpdateSyncStatus(
                        new SyncStatusOptions { },
                        new UpdateSyncStatusOptions { ClearDownloadError = true });
                    break;
            }
        }

        EventStream<EnqueuedCommand>? controlInvocations = null;
        Task? receivingLines = null;

        try
        {
            var options = new
            {
                parameters = resolvedOptions.Params,
                active_streams = activeStreams,
                include_defaults = resolvedOptions.IncludeDefaultStreams,
                app_metadata = resolvedOptions.AppMetadata,
                checkpoint_mode = resolvedOptions.CheckpointMode is CheckpointMode.Requests ? "requests" : "legacy",
            };

            StreamingSyncRequest? establishRequest = null;
            IAsyncEnumerable<EnqueuedCommand>? commands = null;

            foreach (var startInstruction in await InvokePowerSyncControl(PowerSyncControlCommand.START, JsonConvert.SerializeObject(options)))
            {
                if (startInstruction is EstablishSyncStream establish)
                {
                    var invocations = new EventStream<EnqueuedCommand>();
                    controlInvocations = invocations;
                    establishRequest = establish.Request;

                    // Subscribe before anything can emit, else the (possibly synchronous)
                    // "established" event is lost.
                    commands = invocations.ListenAsync(nestedCts.Token);

                    // Wired up here rather than after this loop: a later instruction in this
                    // same batch (FetchCredentials) already needs to enqueue a command.
                    notifyCompletedUploads = () =>
                    {
                        if (!invocations.Closed)
                        {
                            invocations.Emit(new EnqueuedCommand
                            {
                                Command = PowerSyncControlCommand.NOTIFY_CRUD_UPLOAD_COMPLETED
                            });
                        }
                    };
                    handleActiveStreamsChange = () =>
                    {
                        if (!invocations.Closed)
                        {
                            invocations.Emit(new EnqueuedCommand
                            {
                                Command = PowerSyncControlCommand.UPDATE_SUBSCRIPTIONS,
                                Payload = JsonConvert.SerializeObject(activeStreams)
                            });
                        }
                    };
                    notifyTokenRefreshed = () =>
                    {
                        if (!invocations.Closed)
                        {
                            invocations.Emit(new EnqueuedCommand
                            {
                                Command = PowerSyncControlCommand.NOTIFY_TOKEN_REFRESHED
                            });
                        }
                    };

                    if (establish.CheckpointRequest is { } seedRequest)
                    {
                        // Run concurrently so that seeding checkpoint state doesn't block sync line processing.
                        seedingCheckpointState = Task.Run(async () =>
                        {
                            try
                            {
                                await checkpointState.MarkCheckpointsReady(
                                    () => SeedCheckpointRequestState(nestedCts.Token, seedRequest));
                            }
                            catch (OperationCanceledException) { }
                            catch (Exception ex)
                            {
                                // Fail the download iteration if checkpoint requests are broken.
                                if (!invocations.Closed)
                                {
                                    invocations.Emit(new EnqueuedCommand { Error = ex });
                                }
                            }
                        });
                    }
                }
                else if (startInstruction is CloseSyncStream)
                {
                    return new StreamingSyncIterationResult { ImmediateRestart = false };
                }
                else if (startInstruction is NonInterruptingInstruction nonInterrupting)
                {
                    await HandleInstruction(nonInterrupting);
                }
            }

            if (controlInvocations == null)
            {
                return new StreamingSyncIterationResult { ImmediateRestart = false };
            }

            receivingLines = ReceiveSyncLines(establishRequest!, controlInvocations, nestedCts.Token);

            var hadSyncLine = false;
            try
            {
                await foreach (var command in commands!)
                {
                    if (command.Error != null)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(command.Error).Throw();
                    }

                    var close = false;
                    foreach (var instruction in await InvokePowerSyncControl(command.Command, command.Payload))
                    {
                        if (instruction is EstablishSyncStream)
                        {
                            throw new InvalidOperationException("Received EstablishSyncStream while already connected.");
                        }
                        if (instruction is CloseSyncStream closeSyncStream)
                        {
                            hideDisconnectOnRestart = closeSyncStream.HideDisconnect;
                            logger.LogDebug("Closing stream");
                            close = true;
                            break;
                        }
                        if (instruction is NonInterruptingInstruction nonInterrupting)
                        {
                            await HandleInstruction(nonInterrupting);
                        }
                    }

                    if (!hadSyncLine &&
                        (command.Command == PowerSyncControlCommand.PROCESS_TEXT_LINE ||
                         command.Command == PowerSyncControlCommand.PROCESS_BSON_LINE))
                    {
                        // Triggers a local CRUD upload when the first sync line has been received.
                        // This allows uploading local changes that have been made while offline or disconnected.
                        hadSyncLine = true;
                        TriggerCrudUpload();
                    }

                    if (close)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (nestedCts.IsCancellationRequested)
            {
                // Disconnect/abort, stop consuming
            }

            if (streamError != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(streamError).Throw();
            }
        }
        finally
        {
            notifyCompletedUploads = null;
            handleActiveStreamsChange = null;
            notifyTokenRefreshed = null;

            nestedCts.Cancel();
            controlInvocations?.Close();

            if (receivingLines != null)
            {
                try { await receivingLines; } catch { /* surfaced via streamError */ }
            }

            // Let the seed settle before marking the iteration as ended, otherwise a seed completing
            // during teardown could report readiness for an iteration that is already gone.
            if (seedingCheckpointState != null)
            {
                try { await seedingCheckpointState; } catch { /* surfaced via EnqueuedCommand.Error */ }
            }

            // No checkpoint requests can be made until the next iteration seeds its state.
            checkpointState.DownloadIterationEnded();

            await Stop();
        }

        return new StreamingSyncIterationResult { ImmediateRestart = hideDisconnectOnRestart };
    }

    public void Close()
    {
        crudUpdateCts?.Cancel();
        crudUpdateCts = null;
        try { crudUpdateTask?.Wait(2000); } catch (Exception) { }
        Events.Close();
    }

    protected async Task InternalUploadAllCrud(CancellationToken signal, PowerSyncConnectionOptions options)
    {
        await locks.ObtainLock(new LockOptions<Task>
        {
            Type = LockType.CRUD,
            Callback = async () =>
            {
                CrudEntry? checkedCrudItem = null;

                while (!signal.IsCancellationRequested)
                {
                    try
                    {
                        // This is the first item in the FIFO CRUD queue.
                        var nextCrudItem = await Options.Adapter.NextCrudItem();
                        if (nextCrudItem != null)
                        {
                            UpdateSyncStatus(new SyncStatusOptions { DataFlow = new SyncDataFlowStatus { Uploading = true } });

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
                            var neededUpdate = await Options.Adapter.UpdateLocalTarget(() =>
                                options.CheckpointMode is CheckpointMode.Requests
                                    ? RequestNextCheckpointFromService(signal)
                                    : GetLegacyWriteCheckpoint());
                            if (neededUpdate)
                            {
                                notifyCompletedUploads?.Invoke();
                            }
                            else if (checkedCrudItem != null)
                            {
                                // Only log this if there was something to upload
                                logger.LogDebug("Upload complete, no write checkpoint needed.");
                            }
                            break;
                        }
                    }
                    catch (OperationCanceledException) when (signal.IsCancellationRequested)
                    {
                        // Disconnecting.
                        break;
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

                        await DelayRetry(signal, options.RetryDelayMs ?? DEFAULT_RETRY_DELAY_MS);

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

                // Emit events using new SyncStatus objects to prevent local modifications propagating to StreamingSyncImplementation

                // Only trigger this if there was a change
                Events.Emit(new StreamingSyncImplementationEvents.StatusChangedEvent(new SyncStatus(updatedStatus.Options)));

                // Emit StatusUpdated event wrapping a new SyncStatus object (prevents race conditions)
                Events.Emit(new StreamingSyncImplementationEvents.StatusUpdatedEvent(new SyncStatus(updatedStatus.Options)));
            }
            else
            {
                // Emit StatusUpdated event directly wrapping `updatedStatus` (not exposed elsewhere)
                Events.Emit(new StreamingSyncImplementationEvents.StatusUpdatedEvent(updatedStatus));
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Error updating sync status: {message}", ex.Message);
        }
    }

    /// <summary>
    /// Waits out a retry delay. Disconnecting ends the delay rather than throwing: callers check the
    /// signal themselves, and a deliberate disconnect isn't a failure worth surfacing.
    /// </summary>
    /// <param name="resumeOnCheckpointRequest">
    /// When set, the delay also ends as soon as a caller starts waiting to request a checkpoint. Such
    /// a caller needs a seeded download iteration, so there is no point in making it wait out the
    /// full delay.
    /// </param>
    private async Task DelayRetry(CancellationToken signal, int delay, bool resumeOnCheckpointRequest = false)
    {
        if (signal.IsCancellationRequested)
        {
            return;
        }

        using var nestedCts = CancellationTokenSource.CreateLinkedTokenSource(signal);
        var timeout = Options.TimeProvider.Delay(TimeSpan.FromMilliseconds(delay), nestedCts.Token);

        if (resumeOnCheckpointRequest)
        {
            // WhenAny returns the winner without observing it, so neither branch throws here.
            await Task.WhenAny(checkpointState.WaitForCheckpointWaiter(nestedCts.Token), timeout);
        }
        else
        {
            try
            {
                await timeout;
            }
            catch (OperationCanceledException)
            {
                // Disconnected.
            }
        }

        // Ends whichever task is still pending. Without this an abandoned checkpoint waiter would
        // consume the signal that should have woken the next delay.
        nestedCts.Cancel();
    }

    public void UpdateSubscriptions(SubscribedStream[] subscriptions)
    {
        activeStreams = subscriptions;
        handleActiveStreamsChange?.Invoke();
    }

    /// <summary>A conflating single-slot channel: only the fact that a signal arrived matters.</summary>
    private static Channel<T> CreateNotifier<T>() => Channel.CreateBounded<T>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    internal record LegacyWriteCheckpointResponseData(
        [property: JsonProperty("write_checkpoint")] string WriteCheckpoint
    );
    internal record LegacyWriteCheckpointApiResponse(
        [property: JsonProperty("data")] LegacyWriteCheckpointResponseData Data
    );
}

/// <summary>
/// The mechanism to request checkpoints from the PowerSync service.
///
/// Checkpoint requests are used after a client uploads local mutations. The PowerSync service later references them in
/// downloaded data, allowing the SDK to assume that uploaded data has been synced down again.
///
/// There are two ways to send checkpoint requests: A legacy (but default and stable) format supported by all PowerSync
/// service versions, and a newer (`requests`) method which is only available from PowerSync service version 1.24.0 or
/// later.
///
/// Note that the requests checkpoint mode is an alpha API.
/// </summary>
public record CheckpointMode
{
    private CheckpointMode() { }

    /// <summary>
    /// Uses a legacy but stable endpoint to request checkpoints.
    /// </summary>
    public static readonly CheckpointMode Legacy = new();

    /// <summary>
    /// Adopts a new and more efficient checkpoint protocol with better support for switching users
    /// on devices.
    /// </summary>
    public sealed record Requests : CheckpointMode
    {
        const long MINIMUM_RETRY_DELAY = 10_000;
        const long DEFAULT_RETRY_DELAY = MINIMUM_RETRY_DELAY;

        /// <summary>
        /// The periodic interval before re-posting the latest checkpoint request to the service if
        /// it has not been applied in time.
        /// </summary>
        public long RetryDelayMs { get; }

        /// <summary>
        /// Use checkpoint requests with the default retry delay.
        /// </summary>
        public Requests()
        {
            RetryDelayMs = DEFAULT_RETRY_DELAY;
        }

        /// <summary>
        /// Use checkpoint requests with a custom retry delay.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when retry delay is less than <see cref="MINIMUM_RETRY_DELAY" /></exception>
        public Requests(long retryDelayMs)
        {
            if (retryDelayMs < MINIMUM_RETRY_DELAY)
            {
                throw new ArgumentException($"Retry delay must be at least {MINIMUM_RETRY_DELAY}ms.");
            }
            RetryDelayMs = retryDelayMs;
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
