using System.Diagnostics;
using System.Reflection.Metadata;
using System.Runtime.ConstrainedExecution;
using System.Text.RegularExpressions;
using Common.DB;
using Common.DB.Crud;
using Common.DB.Schema;
using Newtonsoft.Json;

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

    private readonly IDBAdapter database;
    private Schema schema;
    private static readonly Regex POWERSYNC_TABLE_MATCH = new Regex(@"(^ps_data__|^ps_data_local__)", RegexOptions.Compiled);

    // Returns true if the connection is closed.    
    public bool Closed;
    public bool Ready;

    protected Task isReadyTask;

    public string SdkVersion;
    public SyncStatus SyncStatus;

    public AbstractPowerSyncDatabase(PowerSyncDatabaseOptions options)
    {
        database = options.Database;
        SyncStatus = new SyncStatus(new SyncStatusOptions());
        Closed = false;
        Ready = false;

        schema = options.Schema;
        SdkVersion = "";
        isReadyTask = Initialize();
    }

    protected async Task Initialize()
    {
        Console.WriteLine("test");
        await LoadVersion();
        await UpdateSchema(schema);
        await UpdateHasSynced();
        await database.Execute("PRAGMA RECURSIVE_TRIGGERS=TRUE");
        Ready = true;
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
    public record VersionResult(string Version);

    private async Task LoadVersion()
    {
        string sdkVersion = (await database.Get<VersionResult>("SELECT powersync_rs_version() as version")).Version;
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
    public record LastSyncedResult(string? synced_at);
    // 
    protected async Task UpdateHasSynced()
    {
        var syncedAt = (await database.Get<LastSyncedResult>("SELECT powersync_last_synced_at() as synced_at")).synced_at;
        var result = await database.Execute("SELECT powersync_last_synced_at() as synced_at");

        Console.WriteLine("Result: " + syncedAt);
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
        Console.WriteLine("Schema update start");
        await database.Execute("SELECT powersync_replace_schema(?)", [schema.ToJson()]);
        await database.RefreshSchema();
        Console.WriteLine("Schema updated!");
        // this.iterateListeners(async (cb) => cb.schemaChanged?.(schema));
    }

    public async Task<QueryResult> Execute(string query, object[]? parameters = null)
    {
        await WaitForReady();
        return await database.Execute(query, parameters);
    }

    public async Task<T?> GetOptional<T>(string query, object[]? parameters = null)
    {
        await WaitForReady();
        return await database.GetOptional<T>(query);
    }
    public async Task<T> Get<T>(string query, object[]? parameters = null)
    {
        await WaitForReady();
        return await database.Get<T>(query);
    }
}