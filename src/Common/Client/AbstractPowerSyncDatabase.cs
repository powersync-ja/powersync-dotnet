namespace Common.Client;

using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Common.Client.Connection;
using Common.Client.Sync.Bucket;
using Common.Client.Sync.Stream;
using Common.DB;
using Common.DB.Crud;
using Common.DB.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public class BasePowerSyncDatabaseOptions()
{
    /**
     * Schema used for the local database.
     */
    public required Schema Schema { get; set; }

    public ILogger? Logger { get; set; }

}

public class PowerSyncDatabaseOptions() : BasePowerSyncDatabaseOptions()
{
    /**
    * Source for a SQLite database connection.
    */
    public required IDBAdapter Database { get; set; }
}

public interface IPowerSyncDatabase
{

}

public class AbstractPowerSyncDatabase : IPowerSyncDatabase
{

    public IDBAdapter Database;
    private Schema schema;
    private static readonly Regex POWERSYNC_TABLE_MATCH = new Regex(@"(^ps_data__|^ps_data_local__)", RegexOptions.Compiled);

    // Returns true if the connection is closed.    
    public bool Closed;
    public bool Ready;

    protected Task isReadyTask;

    private StreamingSyncImplementation? syncStreamImplementation;
    public string SdkVersion;

    protected IBucketStorageAdapter BucketStorageAdapter;

    public SyncStatus SyncStatus;

    public ILogger Logger;

    public AbstractPowerSyncDatabase(PowerSyncDatabaseOptions options)
    {
        Database = options.Database;
        Logger = options.Logger ?? NullLogger.Instance;
        SyncStatus = new SyncStatus(new SyncStatusOptions());
        BucketStorageAdapter = generateBucketStorageAdapter();

        Closed = false;
        Ready = false;

        schema = options.Schema;
        SdkVersion = "";
        isReadyTask = Initialize();
    }

    protected IBucketStorageAdapter generateBucketStorageAdapter()
    {
        return new SqliteBucketStorage(Database);
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

    protected async Task Initialize()
    {
        await BucketStorageAdapter.Init();
        await LoadVersion();
        await UpdateSchema(schema);
        await UpdateHasSynced();
        await Database.Execute("PRAGMA RECURSIVE_TRIGGERS=TRUE");
        Ready = true;

        // TODO CL
        // this.iterateListeners((cb) => cb.initialized?.());
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

    // { synced_at: string | null }
    private record LastSyncedResult(string? synced_at);
    // 
    protected async Task UpdateHasSynced()
    {
        var syncedAt = (await Database.Get<LastSyncedResult>("SELECT powersync_last_synced_at() as synced_at")).synced_at;

        Console.WriteLine("Synced at: " + syncedAt);
        // TODO CL
        // const hasSynced = result.synced_at != null;
        // const syncedAt = result.synced_at != null ? new Date(result.synced_at! + 'Z') : undefined;


        // communicate update
        // if (hasSynced != this.currentStatus.hasSynced) {
        //   this.currentStatus = new SyncStatus({ ...this.currentStatus.toJSON(), hasSynced, lastSyncedAt: syncedAt });
        //   this.iterateListeners((l) => l.statusChanged?.(this.currentStatus));
        // }
    }


    /// Replace the schema with a new version. This is for advanced use cases - typically the schema should just be specified once in the constructor.
    /// Cannot be used while connected - this should only be called before {@link AbstractPowerSyncDatabase.connect}.
    public async Task UpdateSchema(Schema schema)
    {
        // TODO throw on 'connected'
        //  if (this.syncStreamImplementation)
        // {
        //     throw new Error('Cannot update schema while connected');
        // }


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
        // this.iterateListeners(async (cb) => cb.schemaChanged?.(schema));
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

        // TODO CL
        //     this.syncStatusListenerDisposer = this.syncStreamImplementation.registerListener({
        //     statusChanged: (status) =>
        //     {
        //         this.currentStatus = new SyncStatus({
        //       ...status.toJSON(),
        //       hasSynced: this.currentStatus?.hasSynced || !!status.lastSyncedAt
        //     });
        //         this.iterateListeners((cb) => cb.statusChanged?.(this.currentStatus));
        //     }
        // });

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
            syncStreamImplementation.Dispose();
            syncStreamImplementation = null;
        }
        // TODO CL
        // this.syncStatusListenerDisposer?.();
    }

    public async Task DisconnectAndClear()
    {
        await Disconnect();
        await WaitForReady();

        // TODO CL bool clearLocal = options?.ClearLocal ?? false;
        bool clearLocal = true;

        // TODO: Verify necessity of DB name with the extension
        await Database.WriteTransaction(async tx =>
        {
            await tx.Execute("SELECT powersync_clear(?)", [clearLocal ? 1 : 0]);
        });

        // The data has been deleted - reset the sync status
        // this.currentStatus = new SyncStatus({ });
        // this.iterateListeners((l) => l.statusChanged?.(this.currentStatus));
    }

    public async Task Close()
    {
        await WaitForReady();


        // if (options.Disconnect)
        // {
        //     await Disconnect();
        // }

        // if (syncStreamImplementation != null)
        // {
        //     await syncStreamImplementation.DisposeAsync();
        // }

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

    public async Task<QueryResult> Execute(string query, object[]? parameters = null)
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
}