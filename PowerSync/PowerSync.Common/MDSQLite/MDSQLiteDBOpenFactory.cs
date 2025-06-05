namespace PowerSync.Common.MDSQLite;

using PowerSync.Common.Client;
using PowerSync.Common.DB;

public class MDSQLiteOpenFactoryOptions : SQLOpenOptions
{
    public MDSQLiteOptions? SqliteOptions { get; set; }
}

public class MDSQLiteDBOpenFactory : ISQLOpenFactory
{
    private readonly MDSQLiteOpenFactoryOptions options;

    public MDSQLiteDBOpenFactory(MDSQLiteOpenFactoryOptions options)
    {
        this.options = options;
    }

    public IDBAdapter OpenDatabase()
    {
        return new MDSQLiteAdapter(new MDSQLiteAdapterOptions
        {
            Name = options.DbFilename,
            SqliteOptions = options.SqliteOptions
        });
    }
}