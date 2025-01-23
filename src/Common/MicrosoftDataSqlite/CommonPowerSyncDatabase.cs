using Common.Client;

namespace Common.MicrosoftDataSqlite;

public class CommonPowerSyncDatabase(PowerSyncDatabaseOptions options) : AbstractPowerSyncDatabase(options)
{
    public static CommonPowerSyncDatabase Create()
    {
        return new CommonPowerSyncDatabase(new PowerSyncDatabaseOptions(new MDSAdapter()));
    }
}