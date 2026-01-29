using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using PowerSync.Common.Client.Connection;
using PowerSync.Common.Client.Sync.Stream;
using PowerSync.Common.DB.Crud;
using PowerSync.Common.Utils;

namespace PowerSync.Common.Client;

public class ConnectionManagerSyncImplementationResult(
    StreamingSyncImplementation sync,
    Action? onDispose = null)
{
    public StreamingSyncImplementation Sync => sync;
    public Action? OnDispose => onDispose;
}

/// <summary>
/// The subset of <see cref="StreamingSyncImplementationOptions"/> managed by the connection manager.
/// </summary>
public class CreateSyncImplementationOptions : AdditionalConnectionOptions
{
    public SubscribedStream[] Subscriptions { get; init; } = [];
}

public class InternalSubscriptionManager(
    Func<Func<SyncStatus, bool>, CancellationToken?, Task> firstStatusMatching,
    Func<Task> resolveOfflineSyncStatus,
    Func<object, Task> subscriptionsCommand)
{
    public Task FirstStatusMatching(Func<SyncStatus, bool> predicate, CancellationToken? cancellationToken = null)
        => firstStatusMatching(predicate, cancellationToken);

    public Task ResolveOfflineSyncStatus()
        => resolveOfflineSyncStatus();

    public Task SubscriptionsCommand(object payload)
        => subscriptionsCommand(payload);
}

public class ActiveSubscription(
    string name,
    Dictionary<string, object>? parameters,
    ILogger logger,
    Func<CancellationToken?, Task> waitForFirstSync,
    Action clearSubscription)
{
    public int RefCount { get; private set; } = 0;

    public string Name { get; } = name;
    public Dictionary<string, object>? Parameters { get; } = parameters;
    public ILogger Logger { get; } = logger;
    public Func<CancellationToken?, Task> WaitForFirstSync { get; } = waitForFirstSync;

    private readonly Action clearSubscription = clearSubscription;

    public void IncrementRefCount()
    {
        RefCount++;
    }

    public void DecrementRefCount()
    {
        RefCount--;
        if (RefCount == 0)
        {
            clearSubscription();
        }
    }
}

public class StoredConnectionOptions(
    IPowerSyncBackendConnector connector,
    PowerSyncConnectionOptions options)
{
    public IPowerSyncBackendConnector Connector { get; set; } = connector;
    public PowerSyncConnectionOptions Options { get; set; } = options;
}

public class ConnectionManagerEvent
{
    public StreamingSyncImplementation? SyncStreamCreated { get; set; }
}

public class ConnectionManager : EventStream<ConnectionManagerEvent>
{

    /// <summary>
    /// Tracks active connection attempts
    /// </summary>
    protected Task? ConnectingTask;

    /// <summary>
    /// Tracks actively instantiating a streaming sync implementation.
    /// </summary>
    protected Task? SyncStreamInitTask;

    /// <summary>
    /// Active disconnect operation. Calling disconnect multiple times
    /// will resolve to the same operation.
    /// </summary>
    protected Task? DisconnectingTask;

    /// <summary>
    /// Tracks the last parameters supplied to `connect` calls.
    /// Calling `connect` multiple times in succession will result in:
    /// - 1 pending connection operation which will be aborted.
    /// - updating the last set of parameters while waiting for the pending
    ///    attempt to be aborted
    /// - internally connecting with the last set of parameters
    /// </summary>
    protected StoredConnectionOptions? PendingConnectionOptions = null;

    /// <summary>
    /// Subscriptions managed in this connection manager.
    /// </summary>
    private readonly Dictionary<string, ActiveSubscription> locallyActiveSubscriptions = [];

    public StreamingSyncImplementation? SyncStreamImplementation;

    public IPowerSyncBackendConnector? Connector => PendingConnectionOptions?.Connector;


    public PowerSyncConnectionOptions? ConnectionOptions => PendingConnectionOptions?.Options;

    /// <summary>
    /// Additional cleanup function which is called after the sync stream implementation
    /// is disposed.
    /// </summary>
    protected Action? SyncDisposer;

    readonly Func<IPowerSyncBackendConnector, CreateSyncImplementationOptions, Task<ConnectionManagerSyncImplementationResult>> CreateSyncImplementation;

    protected ILogger Logger;

    public ConnectionManager(Func<IPowerSyncBackendConnector, CreateSyncImplementationOptions, Task<ConnectionManagerSyncImplementationResult>> createSyncImplementation,
    ILogger logger)
    {
        CreateSyncImplementation = createSyncImplementation;
        Logger = logger;
        ConnectingTask = null;
        SyncStreamInitTask = null;
        DisconnectingTask = null;
        PendingConnectionOptions = null;
        SyncStreamImplementation = null;
        SyncDisposer = null;
    }

    public new void Close()
    {
        base.Close();
        SyncStreamImplementation?.Close();
        SyncDisposer?.Invoke();
    }

    public async Task Connect(IPowerSyncBackendConnector connector, PowerSyncConnectionOptions options)
    {
        // Keep track if there were pending operations before this call
        var hadPendingOptions = PendingConnectionOptions != null;

        // Update pending options to the latest values
        PendingConnectionOptions = new StoredConnectionOptions(connector, options);


        // Disconnecting here provides aborting in progress connection attempts.
        // The ConnectInternal method will clear pending options once it starts connecting (with the options).
        // We only need to trigger a disconnect here if we have already reached the point of connecting.
        // If we do already have pending options, a disconnect has already been performed.
        // The ConnectInternal method also does a sanity disconnect to prevent straggler connections.
        // We should also disconnect if we have already completed a connection attempt.
        if (!hadPendingOptions || SyncStreamImplementation != null)
        {
            await DisconnectInternal();
        }

        ConnectingTask ??= CheckedConnectInternal();
        await ConnectingTask;
    }

    /// <summary>
    /// Triggers a connect which checks if pending options are available after the connect completes.
    /// The completion can be for a successful, unsuccessful or aborted connection attempt.
    /// If pending options are available another connection will be triggered. 
    /// </summary>
    Task CheckConnection()
    {
        if (PendingConnectionOptions != null)
        {
            // Pending options have been placed while connecting.
            // Need to reconnect.
            ConnectingTask = CheckedConnectInternal();
            return ConnectingTask;
        }
        else
        {
            // Clear the connecting task, done.
            ConnectingTask = null;
            return Task.CompletedTask;
        }
    }

    async Task CheckedConnectInternal()
    {
        try
        {
            await ConnectInternal();
        }
        catch
        {
            // Swallow errors
        }
        finally
        {
            await CheckConnection();
        }
    }

    protected async Task ConnectInternal()
    {
        PowerSyncConnectionOptions? appliedOptions = null;

        // This method ensures a disconnect before any connection attempt
        await DisconnectInternal();

        appliedOptions = PendingConnectionOptions?.Options;
        SyncStreamInitTask = InitSyncStream();

        await SyncStreamInitTask;
        SyncStreamInitTask = null;
        PendingConnectionOptions = null;

        if (appliedOptions == null)
        {
            // A disconnect could have cleared the options.
            return;
        }

        if (DisconnectingTask != null)
        {
            // It might be possible that a disconnect triggered between the last check
            // and this point. Awaiting here allows the sync stream to be cleared if disconnected.
            await DisconnectingTask;
        }

        if (SyncStreamImplementation != null)
        {
            await SyncStreamImplementation.Connect(appliedOptions);
        }
    }

    async Task InitSyncStream()
    {
        if (PendingConnectionOptions == null)
        {
            Logger.LogDebug("No pending connection options found, not creating sync stream implementation");
            // A disconnect could have cleared this.
            return;
        }

        if (DisconnectingTask != null)
        {
            return;
        }

        var connector = PendingConnectionOptions.Connector;
        var options = PendingConnectionOptions.Options;

        var result = await CreateSyncImplementation(connector, new CreateSyncImplementationOptions
        {
            Subscriptions = ActiveStreams,
            CrudUploadThrottleMs = options.CrudUploadThrottleMs,
            RetryDelayMs = options.RetryDelayMs,
        });

        Emit(new ConnectionManagerEvent { SyncStreamCreated = result.Sync });
        SyncStreamImplementation = result.Sync;
        SyncDisposer = result.OnDispose;
        await SyncStreamImplementation.WaitForReady();
    }

    public async Task Disconnect()
    {
        // This will help abort pending connects
        PendingConnectionOptions = null;
        await DisconnectInternal();
    }

    protected async Task DisconnectInternal()
    {
        if (DisconnectingTask != null)
        {
            // A disconnect is already in progress
            await DisconnectingTask;
            return;
        }

        DisconnectingTask = PerformDisconnect();

        await DisconnectingTask;
        DisconnectingTask = null;
    }

    protected async Task PerformDisconnect()
    {
        // Wait if a sync stream implementation is being created before closing it
        // (SyncStreamImplementation must be assigned before we can properly dispose it)
        if (SyncStreamInitTask != null)
        {
            await SyncStreamInitTask;
        }

        // Keep reference to the sync stream implementation and disposer
        // The class members will be cleared before we trigger the disconnect
        // to prevent any further calls to the sync stream implementation.
        var sync = SyncStreamImplementation;
        SyncStreamImplementation = null;
        var disposer = SyncDisposer;
        SyncDisposer = null;

        if (sync != null)
        {
            await sync.Disconnect();
            sync.Close();
        }
        disposer?.Invoke();

    }

    public ISyncStream Stream(InternalSubscriptionManager adapter, string name, Dictionary<string, object>? parameters)
    {
        Task WaitForFirstSync(CancellationToken? cancellationToken = null) =>
            adapter.FirstStatusMatching(s =>
            s.ForStream(
                new ConnectionManagerSyncDescription(name, parameters))?.Subscription.HasSynced == true, cancellationToken
            );

        var stream = new ConnectionManagerSyncStream(
            name: name,
            subscribe: async options =>
            {
                // NOTE: We also run this command if a subscription already exists, because this increases the expiry date
                // (relevant if the app is closed before connecting again, where the last subscribe call determines the ttl).
                await adapter.SubscriptionsCommand(
                    new
                    {
                        subscribe = new
                        {
                            stream = new
                            {
                                name,
                                @params = parameters
                            },
                            ttl = options?.Ttl?.TotalSeconds,
                            priority = options?.Priority?.PriorityNumber
                        }
                    }
                 );

                if (SyncStreamImplementation == null)
                {
                    // We're not connected. So, update the offline sync status to reflect the new subscription.
                    // (With an active iteration, the sync client would include it in its state).
                    await adapter.ResolveOfflineSyncStatus();
                }

                var key = $"{name}|{JsonConvert.SerializeObject(parameters)}";
                var subscription = locallyActiveSubscriptions.TryGetValue(key, out var result) ? result : null;

                if (subscription == null)
                {
                    var clearSubscription = () =>
                    {
                        locallyActiveSubscriptions.Remove(key);
                        SubscriptionsMayHaveChanged();
                    };

                    subscription = new ActiveSubscription(name, parameters, Logger, WaitForFirstSync, clearSubscription);
                    locallyActiveSubscriptions[key] = subscription;
                    SubscriptionsMayHaveChanged();
                }

                return new SyncStreamSubscriptionHandle(subscription);
            },
            unsubscribeAll: async () =>
            {
                await adapter.SubscriptionsCommand(new { unsubscribe = new { name, @params = parameters } });
                SubscriptionsMayHaveChanged();

            },
            parameters: parameters
        );
        return stream;
    }

    public SubscribedStream[] ActiveStreams
    {
        get => locallyActiveSubscriptions.Values
            .Select(a => new SubscribedStream
            {
                Name = a.Name,
                Params = a.Parameters
            })
            .ToArray();
    }

    private void SubscriptionsMayHaveChanged()
    {
        SyncStreamImplementation?.UpdateSubscriptions(ActiveStreams);
    }
}

class ConnectionManagerSyncDescription(string name,
    Dictionary<string, object>? parameters = null) : ISyncStreamDescription
{
    public string Name => name;

    public Dictionary<string, object>? Parameters => parameters;
}

class ConnectionManagerSyncStream(
    string name,
    Func<SyncStreamSubscribeOptions?, Task<ISyncStreamSubscription>> subscribe,
    Func<Task> unsubscribeAll,
    Dictionary<string, object>? parameters = null) : ISyncStream
{
    public string Name => name;
    public Dictionary<string, object>? Parameters => parameters;

    public Task<ISyncStreamSubscription> Subscribe(SyncStreamSubscribeOptions? options = null)
        => subscribe(options);

    public Task UnsubscribeAll()
        => unsubscribeAll();
}

class SyncStreamSubscriptionHandle : ISyncStreamSubscription
{
    private bool Active;
    private readonly ActiveSubscription Subscription;

    public SyncStreamSubscriptionHandle(ActiveSubscription subscription)
    {
        Subscription = subscription;
        Subscription.IncrementRefCount();
        Active = true;
    }

    /// <summary>
    /// Finalizer(~): Logs a warning when the object is GC'd without Unsubscribe() being called.
    /// </summary>
    ~SyncStreamSubscriptionHandle()
    {
        if (Active)
        {
            Subscription.Logger.LogWarning(
                "A subscription to {Name} with params {Params} leaked! Please ensure calling Unsubscribe() when you don't need a subscription anymore.",
                Subscription.Name,
                JsonConvert.SerializeObject(Subscription.Parameters));
        }
    }

    public string Name => Subscription.Name;

    public Dictionary<string, object>? Parameters => Subscription.Parameters;

    public async Task WaitForFirstSync(CancellationToken cancellationToken = default)
    {
        await Subscription.WaitForFirstSync(cancellationToken);
    }

    public void Unsubscribe()
    {
        if (Active)
        {
            Active = false;
            Subscription.DecrementRefCount();
        }
    }
}
