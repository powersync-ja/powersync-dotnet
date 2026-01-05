using PowerSync.Common.DB;

namespace PowerSync.Maui.SQLite;

using PowerSync.Common.Client;
using PowerSync.Common.MDSQLite;


public class MAUISQLiteDBOpenFactory : ISQLOpenFactory
{
    private readonly MDSQLiteOpenFactoryOptions options;

    public MAUISQLiteDBOpenFactory(MDSQLiteOpenFactoryOptions options)
    {
        this.options = options;
    }

    public IDBAdapter OpenDatabase()
    {
        return new MAUISQLiteAdapter(new MDSQLiteAdapterOptions
        {
            Name = options.DbFilename,
            SqliteOptions = options.SqliteOptions
        });
    }
}
