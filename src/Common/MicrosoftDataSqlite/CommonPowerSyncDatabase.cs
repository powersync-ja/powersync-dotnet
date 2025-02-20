using Common.Client;
using Common.DB.Schema;
using Microsoft.Extensions.Logging;

namespace Common.MicrosoftDataSqlite;

public class CommonPowerSyncDatabase(PowerSyncDatabaseOptions options) : AbstractPowerSyncDatabase(options)
{
    public static CommonPowerSyncDatabase Create(Schema schema, string name, ILogger? logger = null)
    {
        return new CommonPowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new MDSAdapter(new MDSAdapterOptions { Name = name }),
            Schema = schema,
            Logger = logger
        });
    }
}

// new MDSAdapter(new MDSAdapterOptions(name)), schema)