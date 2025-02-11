using System.Text.RegularExpressions;
using Common.Client.Sync.Bucket;
using Common.DB;
using Common.DB.Crud;
using Common.DB.Schema;

namespace Common.Client;


public class BasePowerSyncDatabaseOptions(Schema schema)
{
    /**
     * Schema used for the local database.
     */
    public Schema Schema { get; set; } = schema;
}

public class PowerSyncDatabaseOptions(IDBAdapter database, Schema schema) : BasePowerSyncDatabaseOptions(schema)
{
    /**
    * Source for a SQLite database connection.
    */
    public IDBAdapter Database { get; set; } = database;
}

public class AbstractPowerSyncDatabase
{

    public IDBAdapter Database;
    private Schema schema;
    private static readonly Regex POWERSYNC_TABLE_MATCH = new Regex(@"(^ps_data__|^ps_data_local__)", RegexOptions.Compiled);

    // Returns true if the connection is closed.    
    public bool Closed;
    public bool Ready;

    protected Task isReadyTask;

    public string SdkVersion;

    protected IBucketStorageAdapter bucketStorageAdapter;

    public SyncStatus SyncStatus;

    public AbstractPowerSyncDatabase(PowerSyncDatabaseOptions options)
    {
        Database = options.Database;
        SyncStatus = new SyncStatus(new SyncStatusOptions());
        bucketStorageAdapter = generateBucketStorageAdapter();

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
        await bucketStorageAdapter.Init();
        await LoadVersion();
        await UpdateSchema(schema);
        await UpdateHasSynced();
        await Database.Execute("PRAGMA RECURSIVE_TRIGGERS=TRUE");
        Ready = true;
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
        // const hasSynced = result.synced_at != null;
        // const syncedAt = result.synced_at != null ? new Date(result.synced_at! + 'Z') : undefined;


        // communicate update
        // if (hasSynced != this.currentStatus.hasSynced) {
        //   this.currentStatus = new SyncStatus({ ...this.currentStatus.toJSON(), hasSynced, lastSyncedAt: syncedAt });
        //   this.iterateListeners((l) => l.statusChanged?.(this.currentStatus));
        // }
    }

    /**
       * Replace the schema with a new version. This is for advanced use cases - typically the schema should just be specified once in the constructor.
       *
       * Cannot be used while connected - this should only be called before {@link AbstractPowerSyncDatabase.connect}.
       */
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
        catch (Exception)
        {
            // this.options.logger?.Warn('Schema validation failed. Unexpected behaviour could occur', ex);
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

    public void Connect()
    {

    }

    public async Task Disconnect()
    {
        await this.WaitForReady();
        // await this.syncStreamImplementation?.disconnect();
        // this.syncStatusListenerDisposer?.();
        // await this.syncStreamImplementation?.dispose();
        // this.syncStreamImplementation = undefined;

    }

    public async Task DisconnectAndClear()
    {
        await Disconnect();
        await WaitForReady();

        // bool clearLocal = options?.ClearLocal ?? false;
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
        return await bucketStorageAdapter.GetClientId();
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