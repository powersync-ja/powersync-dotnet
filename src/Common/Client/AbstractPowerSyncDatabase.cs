using Common.DB.Crud;

namespace Common.Client;
public abstract class AbstractPowerSyncDatabase {

    // Returns true if the connection is closed.    
    bool closed;
    bool ready;

    string sdkVersion;
    SyncStatus syncStatus;

    public AbstractPowerSyncDatabase() {
        this.syncStatus = new SyncStatus(new SyncStatusOptions());
        this.closed = false;
        this.ready = false;

        this.sdkVersion = "";
    }   
}