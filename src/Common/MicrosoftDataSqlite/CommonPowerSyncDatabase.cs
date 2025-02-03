using Common.Client;
using Common.DB.Schema;

namespace Common.MicrosoftDataSqlite;

public class CommonPowerSyncDatabase(PowerSyncDatabaseOptions options) : AbstractPowerSyncDatabase(options)
{
    public static CommonPowerSyncDatabase Create(Schema schema)
    {
        return new CommonPowerSyncDatabase(new PowerSyncDatabaseOptions(new MDSAdapter(), schema));
    }
}