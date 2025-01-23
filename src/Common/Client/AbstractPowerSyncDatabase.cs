using Common.DB;
using Common.DB.Crud;

namespace Common.Client;

public class PowerSyncDatabaseOptions(IDBAdapter database)
{
    /**
    * Source for a SQLite database connection.
    */
    public IDBAdapter Database { get; set; } = database;
}

public class AbstractPowerSyncDatabase
{

    private IDBAdapter database;

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

        SdkVersion = "";
        isReadyTask = Initialize();
    }

    protected async Task Initialize()
    {
        await LoadVersion();
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

    public async Task<QueryResult> Execute(string query, object[]? parameters = null)
    {
        await WaitForReady();
        return await database.Execute(query);
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