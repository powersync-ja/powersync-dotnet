using Common.DB;
using Common.DB.Crud;

namespace Common.Client;
public class AbstractPowerSyncDatabase
{

    private IDBAdapter database;

    // Returns true if the connection is closed.    
    bool closed;
    bool ready;

    string sdkVersion;
    SyncStatus syncStatus;

    public AbstractPowerSyncDatabase()
    {
        this.syncStatus = new SyncStatus(new SyncStatusOptions());
        this.closed = false;
        this.ready = false;

        this.sdkVersion = "";
        this.Initialize();
    }


    protected async void Initialize()
    {
        await this.loadVersion();
        // await this.database.execute('PRAGMA RECURSIVE_TRIGGERS=TRUE');

    }

    private async Task loadVersion()
    {
        string sdkVersion = "0.2.0";
        this.sdkVersion = sdkVersion;
        int[] versionInts;
        try
        {
            // Parse the version into an array of integers
            versionInts = sdkVersion
                .Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Take(3)
                .Select(n => int.Parse(n))
                .ToArray();
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
}