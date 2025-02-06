using Common.Client;
using Common.DB.Schema;

namespace Common.MicrosoftDataSqlite;

public class CommonPowerSyncDatabase(PowerSyncDatabaseOptions options) : AbstractPowerSyncDatabase(options)
{
    public static CommonPowerSyncDatabase Create(Schema schema, string name)
    {
        return new CommonPowerSyncDatabase(new PowerSyncDatabaseOptions(new MDSAdapter(new MDSAdapterOptions(name)), schema));
    }
}