namespace PowerSync.Common.Client;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Newtonsoft.Json;

using Nito.AsyncEx;

using PowerSync.Common.Client.Connection;
using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.Client.Sync.Stream;
using PowerSync.Common.DB;
using PowerSync.Common.DB.Crud;
using PowerSync.Common.DB.Schema;
using PowerSync.Common.MDSQLite;
using PowerSync.Common.Utils;

public class BasePowerSyncDatabaseOptions()
{
    /// <summary>
    /// Schema used for the local database.
    /// </summary>
    public Schema Schema { get; set; } = null!;

    public ILogger? Logger { get; set; } = null!;

}

public interface IDatabaseSource { }

public class DBAdapterSource(IDBAdapter Adapter) : IDatabaseSource
{
    public IDBAdapter Adapter { get; init; } = Adapter;
}

public class PowerSyncDatabaseOptions() : BasePowerSyncDatabaseOptions()
{
    /// <summary>
    /// Source for a SQLite database connection.
    /// </summary>
    public IDatabaseSource Database { get; set; } = null!;

    /// <summary>
    /// Optional factory for creating the Remote instance.
    /// Used for testing to inject mock implementations.
    /// If not provided, a default Remote will be created.
    /// </summary>
    public Func<IPowerSyncBackendConnector, Remote>? RemoteFactory { get; set; }
}

public class PowerSyncDBEvent : StreamingSyncImplementationEvent
{
    public bool? Initialized { get; set; }
    public Schema? SchemaChanged { get; set; }

    public bool? Closing { get; set; }

    public bool? Closed { get; set; }
}

public interface IPowerSyncDatabase : IEventStream<PowerSyncDBEvent>
{
    public Task Connect(IPowerSyncBackendConnector connector, PowerSyncConnectionOptions? options = null);
    public ISyncStream SyncStream(string name, Dictionary<string, object>? parameters = null);

    public Task<string> GetClientId();

    public Task<CrudBatch?> GetCrudBatch(int limit);

    public Task<CrudTransaction?> GetNextCrudTransaction();

    Task<NonQueryResult> Execute(string query, object?[]? parameters = null);

    Task<NonQueryResult> ExecuteBatch(string query, object?[][]? parameters = null);

    Task<T[]> GetAll<T>(string sql, object?[]? parameters = null);
    Task<dynamic[]> GetAll(string sql, object?[]? parameters = null);

    Task<T?> GetOptional<T>(string sql, object?[]? parameters = null);
    Task<dynamic?> GetOptional(string sql, object?[]? parameters = null);

    Task<T> Get<T>(string sql, object?[]? parameters = null);
    Task<dynamic> Get(string sql, object?[]? parameters = null);

    Task<T> ReadLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null);

    Task<T> ReadTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null);

    Task WriteLock(Func<ILockContext, Task> fn, DBLockOptions? options = null);
    Task<T> WriteLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null);

    Task WriteTransaction(Func<ITransaction, Task> fn, DBLockOptions? options = null);
    Task<T> WriteTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null);

}

public class PowerSyncDatabase : EventStream<PowerSyncDBEvent>, IPowerSyncDatabase
{
    public IDBAdapter Database { get; protected set; }
    private CompiledSchema schema;

    private static readonly int DEFAULT_WATCH_THROTTLE_MS = 30;
    private static readonly Regex POWERSYNC_TABLE_MATCH = new Regex(@"(^ps_data__|^ps_data_local__)", RegexOptions.Compiled);

    public new bool Closed { get; protected set; }
    public bool Ready { get; protected set; }

    protected Task IsReadyTask;
    protected ConnectionManager ConnectionManager;

    private readonly InternalSubscriptionManager subscriptions;

    private StreamingSyncImplementation? syncStreamImplementation;
    public string SdkVersion { get; protected set; }

    protected IBucketStorageAdapter BucketStorageAdapter;

    public SyncStatus CurrentStatus { get; protected set; }

    protected CancellationTokenSource masterCts = new();

    protected CancellationTokenSource? syncStreamStatusCts;

    public ILogger Logger { get; protected set; }

    private readonly AsyncLock runExclusive = new();
    private readonly Func<IPowerSyncBackendConnector, Remote> remoteFactory;

    public StreamingSyncImplementation? SyncStreamImplementation => ConnectionManager.SyncStreamImplementation;

    public IPowerSyncBackendConnector? Connector => ConnectionManager.Connector;

    public bool Connected => CurrentStatus.Connected;
    public bool Connecting => CurrentStatus.Connecting;


    public PowerSyncConnectionOptions? ConnectionOptions => ConnectionManager.ConnectionOptions;

    public PowerSyncDatabase(PowerSyncDatabaseOptions options)
    {
        if (options.Database is DBAdapterSource adapterSource)
        {
            Database = adapterSource.Adapter;
        }
        else if (options.Database is ISQLOpenFactory factorySource)
        {
            Database = factorySource.OpenDatabase();
        }
        else if (options.Database is SQLOpenOptions openOptions)
        {
            Database = new MDSQLiteAdapter(new MDSQLiteAdapterOptions
            {
                Name = openOptions.DbFilename,
                SqliteOptions = null
            });
        }
        else
        {
            throw new ArgumentException("The provided `Database` option is invalid.");
        }
        Logger = options.Logger ?? NullLogger.Instance;
        CurrentStatus = new SyncStatus(new SyncStatusOptions());
        BucketStorageAdapter = generateBucketStorageAdapter();

        Closed = false;
        Ready = false;

        schema = options.Schema.Compile();
        SdkVersion = "";

        remoteFactory = options.RemoteFactory ?? (connector => new Remote(connector));

        // Start async init
        subscriptions = new InternalSubscriptionManager(
            firstStatusMatching: WaitForStatus,
            resolveOfflineSyncStatus: ResolveOfflineSyncStatus,
            subscriptionsCommand: async (payload) => await this.WriteTransaction(async tx =>
                {
                    await tx.Execute("SELECT powersync_control(?, ?) AS r", ["subscriptions", JsonConvert.SerializeObject(payload)]);
                }));

        ConnectionManager = new ConnectionManager(createSyncImplementation: async (connector, options) =>
        {
            await WaitForReady();
            using (await runExclusive.LockAsync())
            {
                syncStreamImplementation = new StreamingSyncImplementation(new StreamingSyncImplementationOptions
                {
                    Adapter = BucketStorageAdapter,
                    Remote = remoteFactory(connector),
                    UploadCrud = async () =>
                    {
                        await WaitForReady();
                        await connector.UploadData(this);
                    },
                    RetryDelayMs = options.RetryDelayMs,
                    Subscriptions = options.Subscriptions,
                    CrudUploadThrottleMs = options.CrudUploadThrottleMs,
                    Logger = Logger
                });

                var syncStreamStatusCts = new CancellationTokenSource();
                var _ = Task.Run(() =>
                {
                    foreach (var update in syncStreamImplementation.Listen(syncStreamStatusCts.Token))
                    {
                        if (update.StatusChanged != null)
                        {
                            CurrentStatus = new SyncStatus(new SyncStatusOptions(update.StatusChanged.Options)
                            {
                                HasSynced = CurrentStatus?.HasSynced == true || update.StatusChanged.LastSyncedAt != null,
                            });
                            Emit(new PowerSyncDBEvent { StatusChanged = CurrentStatus });
                        }
                    }
                });

                await syncStreamImplementation.WaitForReady();
                return new ConnectionManagerSyncImplementationResult(syncStreamImplementation, () => syncStreamStatusCts.Cancel());
            }
        }, logger: Logger);

        IsReadyTask = Initialize(options);
    }

    protected IBucketStorageAdapter generateBucketStorageAdapter()
    {
        return new SqliteBucketStorage(Database, Logger);
    }

    /// <summary>
    /// Resolves once initialization is completed.
    /// </summary>
    public async Task WaitForReady()
    {
        if (Ready)
        {
            return;
        }

        await IsReadyTask;
    }

    public class PrioritySyncRequest
    {
        public CancellationToken? Token { get; set; }
        public int? Priority { get; set; }
    }

    /// <summary>
    /// Wait for the first sync operation to complete.
    /// </summary>
    /// <param name="request">
    /// An object providing a cancellation token and a priority target.
    /// When a priority target is set, the task may complete when all buckets with the given (or higher)
    /// priorities have been synchronized. This can be earlier than a complete sync.
    /// </param>
    /// <returns>A task which will complete once the first full sync has completed.</returns>
    public async Task WaitForFirstSync(PrioritySyncRequest? request = null)
    {
        var priority = request?.Priority;
        var cancellationToken = request?.Token;

        Func<SyncStatus, bool> statusMatches = priority == null
            ? status => status.HasSynced == true
            : status => status.StatusForPriority(priority.Value).HasSynced == true;

        await WaitForStatus(statusMatches, cancellationToken);
    }

    /// <summary>
    /// Waits for the first sync status for which the <paramref name="predicate"/> callback returns true.
    /// </summary>
    /// <param name="predicate">A function that evaluates the sync status and returns true when the desired condition is met.</param>
    /// <param name="cancellationToken">Optional cancellation token to abort the wait.</param>
    public async Task WaitForStatus(Func<SyncStatus, bool> predicate, CancellationToken? cancellationToken = null)
    {
        if (predicate(CurrentStatus))
        {
            return;
        }

        var tcs = new TaskCompletionSource<bool>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(masterCts.Token);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var update in ListenAsync(cts.Token))
                {
                    if (update.StatusChanged != null && predicate(update.StatusChanged))
                    {
                        cts.Cancel();
                        tcs.TrySetResult(true);
                    }
                }
            }
            catch (OperationCanceledException) { }
        });

        cancellationToken?.Register(() =>
        {
            cts.Cancel();
            tcs.TrySetCanceled();
        });

        await tcs.Task;
    }

    protected async Task Initialize(PowerSyncDatabaseOptions options)
    {
        await BucketStorageAdapter.Init();
        await LoadVersion();
        await UpdateSchema(options.Schema);
        await ResolveOfflineSyncStatus();
        await Database.Execute("PRAGMA RECURSIVE_TRIGGERS=TRUE");
        Ready = true;
        Emit(new PowerSyncDBEvent { Initialized = true });
    }

    private record VersionResult(string version);

    private async Task LoadVersion()
    {
        string sdkVersion = (await Database.Get<VersionResult>("SELECT powersync_rs_version() as version")).version;
        SdkVersion = sdkVersion;

        int[] versionInts;
        try
        {
            versionInts = [.. sdkVersion
                .Split(['.', '/'], StringSplitOptions.RemoveEmptyEntries)
                .Take(3)
                .Select(n => int.Parse(n))];
        }
        catch (Exception e)
        {
            throw new Exception(
                $"Unsupported PowerSync extension version. Need >=0.4.5 <1.0.0, got: {sdkVersion}. Details: {e.Message}"
            );
        }

        // Validate version is >= 0.4.5 and < 1.0.0
        if (versionInts[0] != 0 || versionInts[1] < 4 || (versionInts[1] == 4 && versionInts[2] < 5))
        {
            throw new Exception($"Unsupported PowerSync extension version. Need >=0.4.5 <1.0.0, got: {sdkVersion}");
        }
    }

    private record OfflineSyncStatusResult(string r);
    protected async Task ResolveOfflineSyncStatus()
    {
        var result = await Database.Get<OfflineSyncStatusResult>("SELECT powersync_offline_sync_status() as r");
        var parsed = JsonConvert.DeserializeObject<CoreSyncStatus>(result.r);

        var parsedSyncStatus = CoreInstructionHelpers.CoreStatusToSyncStatus(parsed!);
        var updatedStatus = CurrentStatus.CreateUpdatedStatus(parsedSyncStatus);

        if (!updatedStatus.IsEqual(CurrentStatus))
        {
            CurrentStatus = updatedStatus;
            Emit(new PowerSyncDBEvent { StatusChanged = CurrentStatus });
        }
    }

    /// <summary> 
    /// Replace the schema with a new version. This is for advanced use cases - typically the schema should just be specified once in the constructor.
    /// Cannot be used while connected - this should only be called before <see cref="Connect"/>.
    /// </summary>
    public async Task UpdateSchema(Schema schema)
    {
        CompiledSchema compiledSchema = schema.Compile();
        if (syncStreamImplementation != null)
        {
            throw new Exception("Cannot update schema while connected");
        }

        try
        {
            compiledSchema.Validate();
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Schema validation failed. Unexpected behavior could occur: {Exception}", ex);
        }

        this.schema = compiledSchema;
        await Database.Execute("SELECT powersync_replace_schema(?)", [compiledSchema.ToJSON()]);
        await Database.RefreshSchema();
        Emit(new PowerSyncDBEvent { SchemaChanged = schema });
    }

    /// <summary>
    /// Wait for initialization to complete.
    /// While initializing is automatic, this helps to catch and report initialization errors.
    /// </summary>
    public async Task Init()
    {
        await WaitForReady();
    }

    private RequiredAdditionalConnectionOptions resolveConnectionOptions(PowerSyncConnectionOptions? options)
    {
        var defaults = RequiredAdditionalConnectionOptions.DEFAULT_ADDITIONAL_CONNECTION_OPTIONS;
        return new RequiredAdditionalConnectionOptions
        {
            RetryDelayMs = options?.RetryDelayMs ?? defaults.RetryDelayMs,
            CrudUploadThrottleMs = options?.CrudUploadThrottleMs ?? defaults.CrudUploadThrottleMs,
        };
    }

    public async Task Connect(IPowerSyncBackendConnector connector, PowerSyncConnectionOptions? options = null)
    {
        await WaitForReady();

        // close connection if one is open
        await Disconnect();
        if (Closed)
        {
            throw new Exception("Cannot connect using a closed client");
        }

        var resolvedOptions = options ?? new PowerSyncConnectionOptions();

        await ConnectionManager.Connect(connector, resolvedOptions);
    }

    public async Task Disconnect()
    {
        await ConnectionManager.Disconnect();
    }

    /// <summary>
    /// Disconnect and clear the database.
    /// Use this when logging out.
    /// The database can still be queried after this is called, but the tables
    /// would be empty.
    /// 
    /// To preserve data in local-only tables, set clearLocal to false.
    /// </summary>
    public async Task DisconnectAndClear(bool clearLocal = true)
    {
        await Disconnect();
        await WaitForReady();


        await Database.WriteTransaction(async tx =>
        {
            await tx.Execute("SELECT powersync_clear(?)", [clearLocal ? 1 : 0]);
        });

        // The data has been deleted - reset the sync status
        CurrentStatus = new SyncStatus(new SyncStatusOptions());
        Emit(new PowerSyncDBEvent { StatusChanged = CurrentStatus });
    }

    /// <summary>
    /// Create a sync stream to query its status or to subscribe to it.
    /// 
    /// Sync streams are currently in alpha.
    /// </summary>
    public ISyncStream SyncStream(string name, Dictionary<string, object>? parameters = null)
    {
        return ConnectionManager.Stream(subscriptions, name, parameters);
    }

    /// <summary>
    /// Close the database, releasing resources.
    ///
    /// Also disconnects any active connection.
    /// 
    /// Once close is called, this connection cannot be used again - a new one
    /// must be constructed.
    /// </summary>
    public new async Task Close()
    {
        await WaitForReady();

        if (Closed) return;

        Emit(new PowerSyncDBEvent { Closing = true });

        await Disconnect();

        base.Close();

        masterCts.Cancel();
        masterCts.Dispose();

        ConnectionManager.Close();
        BucketStorageAdapter?.Close();

        Database.Close();
        Closed = true;
        Emit(new PowerSyncDBEvent { Closed = true });
    }

    private record UploadQueueStatsSizeCountResult(long size, long count);
    private record UploadQueueStatsCountResult(long count);
    /// <summary>
    /// Get upload queue size estimate and count.
    /// </summary>
    public async Task<UploadQueueStats> GetUploadQueueStats(bool includeSize = false)
    {
        return await ReadTransaction(async tx =>
        {
            if (includeSize)
            {
                var result = await tx.Get<UploadQueueStatsSizeCountResult>(
                    $"SELECT SUM(cast(data as blob) + 20) as size, count(*) as count FROM {PSInternalTable.CRUD}"
                );

                return new UploadQueueStats(result.count, result.size);
            }
            else
            {
                var result = await tx.Get<UploadQueueStatsCountResult>(
                    $"SELECT count(*) as count FROM {PSInternalTable.CRUD}"
                );
                return new UploadQueueStats(result.count);
            }
        });
    }

    /// <summary>
    /// Get a batch of crud data to upload.
    /// <para />
    /// Returns null if there is no data to upload.
    /// <para />
    /// Use this from the <see cref="IPowerSyncBackendConnector.UploadData"/> callback.
    ///
    /// Once the data have been successfully uploaded, call <see cref="CrudBatch.Complete"/> before
    /// requesting the next batch.
    /// <para />
    /// Use <paramref name="limit"/> to specify the maximum number of updates to return in a single
    /// batch.
    /// <para />
    /// This method does include transaction ids in the result, but does not group
    /// data by transaction. One batch may contain data from multiple transactions,
    /// and a single transaction may be split over multiple batches.
    /// </summary>
    public async Task<CrudBatch?> GetCrudBatch(int limit = 100)
    {
        var crudResult = await GetAll<CrudEntryJSON>($"SELECT id, tx_id as transactionId, data FROM {PSInternalTable.CRUD} ORDER BY id ASC LIMIT ?", [limit + 1]);

        var all = crudResult.Select(CrudEntry.FromRow).ToList();

        var haveMore = false;
        if (all.Count > limit)
        {
            all.RemoveAt(all.Count - 1);
            haveMore = true;
        }
        if (all.Count == 0)
        {
            return null;
        }

        var last = all[all.Count - 1];

        return new CrudBatch(
            [.. all],
            haveMore,
            async writeCheckpoint => await HandleCrudCheckpoint(last.ClientId, writeCheckpoint)
     );
    }

    /// <summary>
    /// Get the next recorded transaction to upload.
    /// <para />
    /// Returns null if there is no data to upload.
    ///
    /// Use this from the <see cref="IPowerSyncBackendConnector.UploadData"/> callback.
    /// <para />
    /// Once the data have been successfully uploaded, call <see cref="CrudTransaction.Complete"/> before
    /// requesting the next transaction.
    /// <para />
    /// Unlike <see cref="GetCrudBatch"/>, this only returns data from a single transaction at a time.
    /// All data for the transaction is loaded into memory.
    /// </summary>
    public async Task<CrudTransaction?> GetNextCrudTransaction()
    {
        return await ReadTransaction(async tx =>
        {
            var first = await tx.GetOptional<CrudEntryJSON>(
            $"SELECT id, tx_id AS transactionId, data FROM {PSInternalTable.CRUD} ORDER BY id ASC LIMIT 1");

            if (first == null)
            {
                return null;
            }

            long? txId = first.TransactionId;
            List<CrudEntry> all;

            if (txId == null)
            {

                all = [CrudEntry.FromRow(first)];
            }
            else
            {
                var result = await tx.GetAll<CrudEntryJSON>(
                    $"SELECT id, tx_id as transactionId, data FROM {PSInternalTable.CRUD} WHERE tx_id = ? ORDER BY id ASC",
                    [txId]);

                all = result.Select(CrudEntry.FromRow).ToList();
            }

            var last = all.Last();
            return new CrudTransaction(
                [.. all],
                async writeCheckpoint => await HandleCrudCheckpoint(last.ClientId, writeCheckpoint),
                txId
            );
        });
    }

    public async Task HandleCrudCheckpoint(long lastClientId, string? writeCheckpoint = null)
    {
        await Database.WriteTransaction(async (tx) =>
        {
            await tx.Execute($"DELETE FROM {PSInternalTable.CRUD} WHERE id <= ?", [lastClientId]);
            if (!string.IsNullOrEmpty(writeCheckpoint))
            {
                var check = await tx.GetAll<object>($"SELECT 1 FROM {PSInternalTable.CRUD} LIMIT 1");
                if (check.Length == 0)
                {

                    await tx.Execute($"UPDATE {PSInternalTable.BUCKETS} SET target_op = CAST(? as INTEGER) WHERE name='$local'", [
                      writeCheckpoint
                    ]);
                }
            }
            else
            {
                await tx.Execute(
                    $"UPDATE {PSInternalTable.BUCKETS} SET target_op = CAST(? as INTEGER) WHERE name = '$local'",
                    [BucketStorageAdapter.GetMaxOpId()]);
            }
        });
    }

    /// <summary>
    /// Get an unique client id for this database.
    ///
    /// The id is not reset when the database is cleared, only when the database is deleted.
    /// </summary>
    public async Task<string> GetClientId()
    {
        return await BucketStorageAdapter.GetClientId();
    }

    public async Task<NonQueryResult> Execute(string query, object?[]? parameters = null)
    {
        await WaitForReady();
        return await Database.Execute(query, parameters);
    }

    public async Task<NonQueryResult> ExecuteBatch(string query, object?[][]? parameters = null)
    {
        await WaitForReady();
        return await Database.ExecuteBatch(query, parameters);
    }

    public async Task<T[]> GetAll<T>(string query, object?[]? parameters = null)
    {
        await WaitForReady();
        return await Database.GetAll<T>(query, parameters);
    }

    public async Task<dynamic[]> GetAll(string query, object?[]? parameters = null)
    {
        await WaitForReady();
        return await Database.GetAll(query, parameters);
    }

    public async Task<T?> GetOptional<T>(string query, object?[]? parameters = null)
    {
        await WaitForReady();
        return await Database.GetOptional<T>(query, parameters);
    }

    public async Task<dynamic?> GetOptional(string query, object?[]? parameters = null)
    {
        await WaitForReady();
        return await Database.GetOptional(query, parameters);
    }

    public async Task<T> Get<T>(string query, object?[]? parameters = null)
    {
        await WaitForReady();
        return await Database.Get<T>(query, parameters);
    }

    public async Task<dynamic> Get(string query, object?[]? parameters = null)
    {
        await WaitForReady();
        return await Database.Get(query, parameters);
    }

    public async Task<T> ReadLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null)
    {
        await WaitForReady();
        return await Database.ReadLock(fn, options);
    }

    public async Task WriteLock(Func<ILockContext, Task> fn, DBLockOptions? options = null)
    {
        await WaitForReady();
        await Database.WriteLock(fn, options);
    }

    public async Task<T> WriteLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null)
    {
        await WaitForReady();
        return await Database.WriteLock(fn, options);
    }

    public async Task<T> ReadTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null)
    {
        await WaitForReady();
        return await Database.ReadTransaction(fn, options);
    }

    public async Task WriteTransaction(Func<ITransaction, Task> fn, DBLockOptions? options = null)
    {
        await WaitForReady();
        await Database.WriteTransaction(fn, options);
    }

    public async Task<T> WriteTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null)
    {
        await WaitForReady();
        return await Database.WriteTransaction(fn, options);
    }

    public IAsyncEnumerable<WatchOnChangeEvent> OnChange(SQLWatchOptions? options = null)
    {
        options ??= new SQLWatchOptions();

        var tables = options?.Tables ?? [];
        var powersyncTables = new HashSet<string>(
            tables.SelectMany(table => new[] { $"ps_data__{table}", $"ps_data_local__{table}" })
        );

        var signal = options?.Signal != null
            ? CancellationTokenSource.CreateLinkedTokenSource(masterCts.Token, options.Signal.Value)
            : CancellationTokenSource.CreateLinkedTokenSource(masterCts.Token);

        var listener = Database.ListenAsync(signal.Token);

        // Return the actual IAsyncEnumerable here, using OnChange as a synchronous wrapper that blocks until the
        // connection is established
        return OnChangeCore(powersyncTables, listener, signal.Token, options?.TriggerImmediately == true);
    }

    private async IAsyncEnumerable<WatchOnChangeEvent> OnChangeCore(
        HashSet<string> watchedTables,
        IAsyncEnumerable<DBAdapterEvent> listener,
        [EnumeratorCancellation] CancellationToken signal,
        bool triggerImmediately
    )
    {
        if (triggerImmediately == true)
        {
            yield return new WatchOnChangeEvent { ChangedTables = [] };
        }

        HashSet<string> changedTables = new();
        await foreach (var e in listener)
        {
            if (signal.IsCancellationRequested) yield break;
            if (e.TablesUpdated == null) continue;

            changedTables.Clear();
            GetTablesFromNotification(e.TablesUpdated, changedTables);
            changedTables.IntersectWith(watchedTables);

            if (changedTables.Count == 0) continue;

            var update = new WatchOnChangeEvent { ChangedTables = [.. changedTables] };

            // Convert from 'ps_data__<name>' to '<name>'
            for (int i = 0; i < update.ChangedTables.Length; i++)
            {
                update.ChangedTables[i] = InternalToFriendlyTableName(update.ChangedTables[i]);
            }
            yield return update;
        }
    }

    private static string InternalToFriendlyTableName(string internalName)
    {
        const string PS_DATA_PREFIX = "ps_data__";
        const string PS_DATA_LOCAL_PREFIX = "ps_data_local__";

        if (internalName.StartsWith(PS_DATA_PREFIX))
            return internalName.Substring(PS_DATA_PREFIX.Length);

        if (internalName.StartsWith(PS_DATA_LOCAL_PREFIX))
            return internalName.Substring(PS_DATA_LOCAL_PREFIX.Length);

        return internalName;
    }

    public IAsyncEnumerable<T[]> Watch<T>(
        string sql,
        object?[]? parameters = null,
        SQLWatchOptions? options = null
    )
    {
        options ??= new SQLWatchOptions();

        // Stop watching on master CTS cancellation, or on user CTS cancellation
        var signal = options.Signal != null
            ? CancellationTokenSource.CreateLinkedTokenSource(masterCts.Token, options.Signal.Value)
            : CancellationTokenSource.CreateLinkedTokenSource(masterCts.Token);

        // Establish the initial DB listener synchronously before returning the IAsyncEnumerable,
        // so that table changes between Watch() being called and iteration starting are not missed.
        // This mirrors the pattern used in OnChange().
        var initialRestartCts = CancellationTokenSource.CreateLinkedTokenSource(signal.Token);
        var initialListener = Database.ListenAsync(initialRestartCts.Token);

        return WatchCore<T>(sql, parameters, options, signal, initialRestartCts, initialListener);
    }

    private async IAsyncEnumerable<T[]> WatchCore<T>(
        string sql,
        object?[]? parameters,
        SQLWatchOptions options,
        CancellationTokenSource signal,
        CancellationTokenSource initialRestartCts,
        IAsyncEnumerable<DBAdapterEvent> initialListener
    )
    {
        var schemaChanged = new TaskCompletionSource<bool>();

        // Listen for schema changes in the background
        _ = Task.Run(async () =>
        {
            await foreach (var update in ListenAsync(signal.Token))
            {
                if (update.SchemaChanged != null)
                {
                    // Swap schemaChanged with an unresolved TCS
                    var oldTcs = Interlocked.Exchange(ref schemaChanged, new());
                    oldTcs.TrySetResult(true);
                }
            }
        }, signal.Token);

        // Re-register query on schema updates
        bool isRestart = false;
        var currentRestartCts = initialRestartCts;
        var currentListener = initialListener;

        while (!signal.Token.IsCancellationRequested)
        {
            // Resolve tables
            HashSet<string> powersyncTables;
            if (options?.Tables != null)
            {
                powersyncTables = [.. options
                    .Tables
                    .SelectMany<string, string>(table => [$"ps_data__{table}", $"ps_data_local__{table}"]
                )];
            }
            else
            {
                powersyncTables = await GetSourceTables(sql, parameters);
            }

            var enumerator = OnRawTableChange(
                powersyncTables,
                currentListener,
                currentRestartCts.Token,
                isRestart || (options?.TriggerImmediately == true)
            ).GetAsyncEnumerator(currentRestartCts.Token);

            // Continually wait for either OnChange or SchemaChanged to fire
            while (true)
            {
                var currentSchemaTask = schemaChanged.Task;
                var onChangeTask = enumerator.MoveNextAsync().AsTask();
                var completedTask = await Task.WhenAny(onChangeTask, currentSchemaTask);

                if (completedTask == currentSchemaTask)
                {
                    currentRestartCts.Cancel();
                    isRestart = true;
                    // Let the current task complete/cancel gracefully
                    try { await onChangeTask; }
                    catch (OperationCanceledException) { }

                    // Establish a new listener BEFORE resolving source tables in the next iteration,
                    // so that changes during the async GetSourceTables call are not missed.
                    currentRestartCts = CancellationTokenSource.CreateLinkedTokenSource(signal.Token);
                    currentListener = Database.ListenAsync(currentRestartCts.Token);

                    break;
                }

                var update = enumerator.Current;
                if (update.ChangedTables != null)
                {
                    if (signal.IsCancellationRequested) yield break;
                    yield return await GetAll<T>(sql, parameters);
                }
            }
        }
    }

    private class ExplainedResult
    {
        public int addr = 0;
        public string opcode = "";
        public int p1 = 0;
        public int p2 = 0;
        public int p3 = 0;
        public string p4 = "";
        public int p5 = 0;
    }
    private record TableSelectResult(string tbl_name);
    internal async Task<HashSet<string>> GetSourceTables(string sql, object?[]? parameters = null)
    {
        var explained = await GetAll<ExplainedResult>(
            $"EXPLAIN {sql}", parameters
        );

        var rootPages = explained
            .Where(row => row.opcode == "OpenRead" && row.p3 == 0)
            .Select(row => row.p2)
            .ToList();

        var tables = await GetAll<TableSelectResult>(
            "SELECT DISTINCT tbl_name FROM sqlite_master WHERE rootpage IN (SELECT json_each.value FROM json_each(?))",
            [JsonConvert.SerializeObject(rootPages)]
        );

        return [.. tables.Select(row => row.tbl_name)];
    }

    private async IAsyncEnumerable<WatchOnChangeEvent> OnRawTableChange(
        HashSet<string> watchedTables,
        IAsyncEnumerable<DBAdapterEvent> listener,
        [EnumeratorCancellation] CancellationToken token,
        bool triggerImmediately = false
    )
    {
        if (triggerImmediately)
        {
            yield return new WatchOnChangeEvent { ChangedTables = [] };
        }

        HashSet<string> changedTables = new();
        await foreach (var e in listener)
        {
            if (e.TablesUpdated != null)
            {
                if (token.IsCancellationRequested) break;

                // Extract the changed tables and intersect with the watched tables
                changedTables.Clear();
                GetTablesFromNotification(e.TablesUpdated, changedTables);
                changedTables.IntersectWith(watchedTables);

                if (changedTables.Count == 0) continue;

                yield return new WatchOnChangeEvent { ChangedTables = [.. changedTables] };
            }
        }
    }

    private static void GetTablesFromNotification(INotification updateNotification, HashSet<string> changedTables)
    {
        string[] tables = [];
        if (updateNotification is BatchedUpdateNotification batchedUpdate)
        {
            tables = batchedUpdate.Tables;
        }
        else if (updateNotification is UpdateNotification singleUpdate)
        {
            tables = [singleUpdate.Table];
        }

        foreach (var table in tables)
        {
            changedTables.Add(table);
        }
    }
}

public class SQLWatchOptions
{
    public CancellationToken? Signal { get; set; }
    public string[]? Tables { get; set; }

    /// <summary>
    /// The minimum interval between queries in milliseconds.
    /// </summary>
    public int? ThrottleMs { get; set; }

    public bool TriggerImmediately { get; set; } = false;
}

public class WatchOnChangeEvent
{
    public string[] ChangedTables { get; set; } = [];
}

public class WatchOnChangeHandler
{
    public Func<WatchOnChangeEvent, Task> OnChange { get; set; } = null!;
    public Action<Exception>? OnError { get; set; }
}

