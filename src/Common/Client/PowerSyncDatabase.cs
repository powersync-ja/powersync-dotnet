namespace Common.Client;

using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Common.Client.Connection;
using Common.Client.Sync.Bucket;
using Common.Client.Sync.Stream;
using Common.DB;
using Common.DB.Crud;
using Common.DB.Schema;
using Common.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

public class BasePowerSyncDatabaseOptions()
{
    /// Schema used for the local database.
    public Schema Schema { get; set; } = null!;

    public ILogger? Logger { get; set; } = null!;

}

public class PowerSyncDatabaseOptions() : BasePowerSyncDatabaseOptions()
{
    /// Source for a SQLite database connection.
    public IDBAdapter Database { get; set; } = null!;
}

public class PowerSyncDBEvent : StreamingSyncImplementationEvent
{
    public bool? Initialized { get; set; }
    public Schema? SchemaChanged { get; set; }
}

public interface IPowerSyncDatabase : IEventStream<PowerSyncDBEvent>
{

}

public class PowerSyncDatabase : EventStream<PowerSyncDBEvent>, IPowerSyncDatabase
{

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
        Database = options.Database;
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

        var tcs = new TaskCompletionSource();
        var cts = new CancellationTokenSource();

        var _ = Task.Run(() =>
        {
            foreach (var update in Listen(cts.Token))
            {
                if (update.StatusChanged?.HasSynced == true)
                {
                    cts.Cancel();
                    tcs.SetResult();
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

    private record LastSyncedResult(string? synced_at);

    protected async Task UpdateHasSynced()
    {
        var result = await Database.Get<LastSyncedResult>("SELECT powersync_last_synced_at() as synced_at");

        var hasSynced = result.synced_at != null;
        DateTime? syncedAt = result.synced_at != null ? DateTime.Parse(result.synced_at + "Z") : null;

        if (hasSynced != CurrentStatus.HasSynced)
        {
            CurrentStatus = new SyncStatus(new SyncStatusOptions(CurrentStatus.Options)
            {
                HasSynced = hasSynced,
                LastSyncedAt = syncedAt
            });

            Emit(new PowerSyncDBEvent { StatusChanged = CurrentStatus });
        }
    }

    /// Replace the schema with a new version. This is for advanced use cases - typically the schema should just be specified once in the constructor.
    /// Cannot be used while connected - this should only be called before {@link AbstractPowerSyncDatabase.connect}.
    public async Task UpdateSchema(Schema schema)
    {
        if (syncStreamImplementation != null)
        {
            throw new Exception("Cannot update schema while connected");
        }

        try
        {
            // schema.Validate();
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

    /// Wait for initialization to complete.
    /// While initializing is automatic, this helps to catch and report initialization errors.
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

    public async Task DisconnectAndClear()
    {
        await Disconnect();
        await WaitForReady();

        // TODO CL bool clearLocal = options?.ClearLocal ?? false;
        bool clearLocal = true;

        await Database.WriteTransaction(async tx =>
        {
            await tx.Execute("SELECT powersync_clear(?)", [clearLocal ? 1 : 0]);
        });

        // The data has been deleted - reset the sync status
        CurrentStatus = new SyncStatus(new SyncStatusOptions());
        Emit(new PowerSyncDBEvent { StatusChanged = CurrentStatus });
    }

    public new async Task Close()
    {
        base.Close();
        await WaitForReady();


        // if (options.Disconnect)
        // {
        //     await Disconnect();
        // }

        syncStreamImplementation?.Close();
        BucketStorageAdapter?.Close();

        Database.Close();
        Closed = true;
    }


    /// Get an unique client id for this database.
    ///
    /// The id is not reset when the database is cleared, only when the database is deleted.
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

    public void Watch(string query, object[]? parameters, WatchHandler handler, SQLWatchOptions? options = null)
    {
        var _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        if (_handler.OnResult == null)
        {
            throw new ArgumentException("onResult is required", nameof(handler));
        }

        Task.Run(async () =>
        {
            var resolvedTables = await ResolveTables(query, parameters, options);
            // Fetch initial data
            // TODO CL typing for GetAll
            var result = await GetAll<object>(query, parameters);
            _handler.OnResult(result);

            OnChange(new WatchOnChangeHandler
            {
                OnChange = async (change) =>
                {
                    try
                    {
                        var result = await GetAll<object>(query, parameters);
                        _handler.OnResult(result);
                    }
                    catch (Exception ex)
                    {
                        _handler.OnError?.Invoke(ex);
                    }
                },
                OnError = _handler.OnError
            }, new SQLWatchOptions
            {
                Tables = resolvedTables,
                Signal = options?.Signal,
                ThrottleMs = options?.ThrottleMs
            });
        });

        // TODO CL cancel?
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

            // TODO CL do we need this: && row.p2 is int
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

    public void OnChange(WatchOnChangeHandler handler, SQLWatchOptions? options = null)
    {
        var _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        if (_handler.OnChange == null)
        {
            throw new ArgumentException("onChange is required", nameof(handler));
        }
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
                _handler.OnChange(new WatchOnChangeEvent { ChangedTables = intersection });
            });
        }

        Database.RunListener((update) =>
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
                    _handler?.OnError?.Invoke(ex);
                }
            }
        });

        // TODO CL pass CTS along
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

public class WatchHandler
{
    public Action<object[]> OnResult { get; set; } = null!;
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