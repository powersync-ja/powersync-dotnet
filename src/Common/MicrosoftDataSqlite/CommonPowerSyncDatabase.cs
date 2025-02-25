namespace Common.MicrosoftDataSqlite;

using Common.Client;
using Common.DB.Schema;
using Microsoft.Extensions.Logging;


public class CommonPowerSyncDatabase(PowerSyncDatabaseOptions options) : PowerSyncDatabase(options)
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