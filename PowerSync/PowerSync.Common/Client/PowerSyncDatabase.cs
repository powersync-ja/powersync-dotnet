namespace PowerSync.Common.Client;

using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Newtonsoft.Json;

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
}

public class PowerSyncDBEvent : StreamingSyncImplementationEvent
{
    public bool? Initialized { get; set; }
    public Schema? SchemaChanged { get; set; }
}

public interface IPowerSyncDatabase : IEventStream<PowerSyncDBEvent>
{
    public Task Connect(IPowerSyncBackendConnector connector, PowerSyncConnectionOptions? options = null);
    public Task<string> GetClientId();

    public Task<CrudBatch?> GetCrudBatch(int limit);

    public Task<CrudTransaction?> GetNextCrudTransaction();

    Task<NonQueryResult> Execute(string query, object[]? parameters = null);

    Task<T[]> GetAll<T>(string sql, params object[]? parameters);

    Task<T?> GetOptional<T>(string sql, params object[]? parameters);

    Task<T> Get<T>(string sql, params object[]? parameters);

    Task<T> ReadLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null);

    Task<T> ReadTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null);

    Task WriteLock(Func<ILockContext, Task> fn, DBLockOptions? options = null);
    Task<T> WriteLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null);

    Task WriteTransaction(Func<ITransaction, Task> fn, DBLockOptions? options = null);
    Task<T> WriteTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null);

}

public class PowerSyncDatabase : EventStream<PowerSyncDBEvent>, IPowerSyncDatabase
{
    private static readonly int FULL_SYNC_PRIORITY = 2147483647;

    public IDBAdapter Database;
    private Schema schema;

    private static readonly int DEFAULT_WATCH_THROTTLE_MS = 30;
    private static readonly Regex POWERSYNC_TABLE_MATCH = new Regex(@"(^ps_data__|^ps_data_local__)", RegexOptions.Compiled);

    public bool Closed;
    public bool Ready;

    protected Task isReadyTask;

    private StreamingSyncImplementation? syncStreamImplementation;
    public string SdkVersion;

    protected IBucketStorageAdapter BucketStorageAdapter;

    protected CancellationTokenSource? syncStreamStatusCts;

    protected SyncStatus CurrentStatus;

    public ILogger Logger;

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

        schema = options.Schema;
        SdkVersion = "";
        isReadyTask = Initialize();
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

        await isReadyTask;
    }

    public async Task WaitForFirstSync(CancellationToken? cancellationToken = null)
    {
        if (CurrentStatus.HasSynced == true)
        {
            return;
        }

        var tcs = new TaskCompletionSource<bool>();
        var cts = new CancellationTokenSource();

        var _ = Task.Run(() =>
        {
            foreach (var update in Listen(cts.Token))
            {
                if (update.StatusChanged?.HasSynced == true)
                {
                    cts.Cancel();
                    tcs.SetResult(true);
                }
            }
        });

        cancellationToken?.Register(() =>
        {
            cts.Cancel();
            tcs.SetCanceled();
        });

        await tcs.Task;
    }

    protected async Task Initialize()
    {
        await BucketStorageAdapter.Init();
        await LoadVersion();
        await UpdateSchema(schema);
        await UpdateHasSynced();
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
                $"Unsupported PowerSync extension version. Need >=0.2.0 <1.0.0, got: {sdkVersion}. Details: {e.Message}"
            );
        }

        // Validate version is >= 0.2.0 and < 1.0.0
        if (versionInts[0] != 0 || versionInts[1] < 2 || versionInts[2] < 0)
        {
            throw new Exception($"Unsupported PowerSync extension version. Need >=0.2.0 <1.0.0, got: {sdkVersion}");
        }
    }

    private record LastSyncedResult(int? priority, string? last_synced_at);

    protected async Task UpdateHasSynced()
    {
        var results = await Database.GetAll<LastSyncedResult>(
            "SELECT priority, last_synced_at FROM ps_sync_state ORDER BY priority DESC"
        );

        DateTime? lastCompleteSync = null;
        
        // TODO: Will be altered/extended when reporting individual sync priority statuses is supported 
        foreach (var result in results)
        {
            var parsedDate = DateTime.Parse(result.last_synced_at + "Z");

            if (result.priority == FULL_SYNC_PRIORITY)
            {
                // This lowest-possible priority represents a complete sync.
                lastCompleteSync = parsedDate;
            }
        }

        var hasSynced = lastCompleteSync != null;
        if (hasSynced != CurrentStatus.HasSynced)
        {
            CurrentStatus = new SyncStatus(new SyncStatusOptions(CurrentStatus.Options)
            {
                HasSynced = hasSynced,
                LastSyncedAt = lastCompleteSync,
            });

            Emit(new PowerSyncDBEvent { StatusChanged = CurrentStatus });
        }
    }

    /// <summary> 
    /// Replace the schema with a new version. This is for advanced use cases - typically the schema should just be specified once in the constructor.
    /// Cannot be used while connected - this should only be called before <see cref="Connect"/>.
    /// </summary>
    public async Task UpdateSchema(Schema schema)
    {
        if (syncStreamImplementation != null)
        {
            throw new Exception("Cannot update schema while connected");
        }

        try
        {
            schema.Validate();
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Schema validation failed. Unexpected behavior could occur: {Exception}", ex);
        }

        this.schema = schema;
        await Database.Execute("SELECT powersync_replace_schema(?)", [schema.ToJSON()]);
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

        var resolvedOptions = resolveConnectionOptions(options);
        syncStreamImplementation = new StreamingSyncImplementation(new StreamingSyncImplementationOptions
        {
            Adapter = BucketStorageAdapter,
            Remote = new Remote(connector),
            UploadCrud = async () =>
            {
                await WaitForReady();
                await connector.UploadData(this);
            },
            RetryDelayMs = resolvedOptions.RetryDelayMs,
            CrudUploadThrottleMs = resolvedOptions.CrudUploadThrottleMs,
            Logger = Logger
        });

        syncStreamStatusCts = new CancellationTokenSource();
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
        syncStreamImplementation.TriggerCrudUpload();
        await syncStreamImplementation.Connect(options);
    }

    public async Task Disconnect()
    {
        await WaitForReady();
        if (syncStreamImplementation != null)
        {
            await syncStreamImplementation.Disconnect();
            syncStreamImplementation.Close();
            syncStreamImplementation = null;
        }
        syncStreamStatusCts?.Cancel();
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


        await Disconnect();
        base.Close();
        syncStreamImplementation?.Close();
        BucketStorageAdapter?.Close();

        Database.Close();
        Closed = true;
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
        var crudResult = await GetAll<CrudEntryJSON>($"SELECT id, tx_id, data FROM {PSInternalTable.CRUD} ORDER BY id ASC LIMIT ?", [limit + 1]);

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
            $"SELECT id, tx_id, data FROM {PSInternalTable.CRUD} ORDER BY id ASC LIMIT 1");

            if (first == null)
            {
                return null;
            }

            long? txId = first.TransactionId ?? null;
            List<CrudEntry> all;

            if (txId == null)
            {

                all = [CrudEntry.FromRow(first)];
            }
            else
            {
                var result = await tx.GetAll<CrudEntryJSON>(
                    $"SELECT id, tx_id, data FROM {PSInternalTable.CRUD} WHERE tx_id = ? ORDER BY id ASC",
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

    public async Task<NonQueryResult> Execute(string query, object[]? parameters = null)
    {
        await WaitForReady();
        return await Database.Execute(query, parameters);
    }

    public async Task<T[]> GetAll<T>(string query, object[]? parameters = null)
    {
        await WaitForReady();
        return await Database.GetAll<T>(query, parameters);
    }

    public async Task<T?> GetOptional<T>(string query, object[]? parameters = null)
    {
        await WaitForReady();
        return await Database.GetOptional<T>(query, parameters);
    }
    public async Task<T> Get<T>(string query, object[]? parameters = null)
    {
        await WaitForReady();
        return await Database.Get<T>(query, parameters);
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

    /// <summary>
    /// Executes a read query every time the source tables are modified.
    /// <para />
    /// Use <see cref="SQLWatchOptions.ThrottleMs"/> to specify the minimum interval between queries.
    /// Source tables are automatically detected using <c>EXPLAIN QUERY PLAN</c>.
    /// </summary>
    public Task Watch<T>(string query, object[]? parameters, WatchHandler<T> handler, SQLWatchOptions? options = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        Task.Run(async () =>
        {
            try
            {
                var resolvedTables = await ResolveTables(query, parameters, options);
                var result = await GetAll<T>(query, parameters);
                handler.OnResult(result);

                OnChange(new WatchOnChangeHandler
                {
                    OnChange = async (change) =>
                    {
                        try
                        {
                            var result = await GetAll<T>(query, parameters);
                            handler.OnResult(result);
                        }
                        catch (Exception ex)
                        {
                            handler.OnError?.Invoke(ex);
                        }
                    },
                    OnError = handler.OnError
                }, new SQLWatchOptions
                {
                    Tables = resolvedTables,
                    Signal = options?.Signal,
                    ThrottleMs = options?.ThrottleMs
                });
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                handler.OnError?.Invoke(ex);
            }
        });
        return tcs.Task;
    }

    private record ExplainedResult(string opcode, int p2, int p3);
    private record TableSelectResult(string tbl_name);
    public async Task<string[]> ResolveTables(string sql, object[]? parameters = null, SQLWatchOptions? options = null)
    {
        List<string> resolvedTables = options?.Tables != null ? [.. options.Tables] : [];

        if (options?.Tables == null)
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

            foreach (var table in tables)
            {
                resolvedTables.Add(POWERSYNC_TABLE_MATCH.Replace(table.tbl_name, ""));
            }
        }

        return [.. resolvedTables];
    }

    /// <summary>
    /// Invokes the provided callback whenever any of the specified tables are modified.
    /// <para />
    /// This is preferred over <see cref="Watch"/> when multiple queries need to be performed
    /// together in response to data changes.
    /// </summary>
    public void OnChange(WatchOnChangeHandler handler, SQLWatchOptions? options = null)
    {
        var resolvedOptions = options ?? new SQLWatchOptions();

        string[] tables = resolvedOptions.Tables ?? [];
        HashSet<string> watchedTables = [.. tables.SelectMany(table => new[] { table, $"ps_data__{table}", $"ps_data_local__{table}" })];

        var changedTables = new HashSet<string>();
        var resolvedThrottleMs = resolvedOptions.ThrottleMs ?? DEFAULT_WATCH_THROTTLE_MS;

        void flushTableUpdates()
        {
            HandleTableChanges(changedTables, watchedTables, (intersection) =>
            {
                if (resolvedOptions?.Signal?.IsCancellationRequested == true) return;
                handler.OnChange(new WatchOnChangeEvent { ChangedTables = intersection });
            });
        }

        var cts = Database.RunListener((update) =>
        {
            if (update.TablesUpdated != null)
            {
                try
                {
                    ProcessTableUpdates(update.TablesUpdated, changedTables);
                    flushTableUpdates();
                }
                catch (Exception ex)
                {
                    handler?.OnError?.Invoke(ex);
                }
            }
        });

        if (options?.Signal.HasValue == true)
        {
            options.Signal.Value.Register(() =>
            {
                cts.Cancel();
            });
        }
    }

    private static void HandleTableChanges(HashSet<string> changedTables, HashSet<string> watchedTables, Action<string[]> onDetectedChanges)
    {
        if (changedTables.Count > 0)
        {
            var intersection = changedTables.Where(watchedTables.Contains).ToArray();
            if (intersection.Length > 0)
            {
                onDetectedChanges(intersection);
            }
        }
        changedTables.Clear();
    }

    private static void ProcessTableUpdates(INotification updateNotification, HashSet<string> changedTables)
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
}

public class WatchHandler<T>
{
    public Action<T[]> OnResult { get; set; } = null!;
    public Action<Exception>? OnError { get; set; }
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