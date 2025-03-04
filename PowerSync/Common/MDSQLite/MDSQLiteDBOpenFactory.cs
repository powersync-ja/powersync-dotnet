namespace Common.MDSQLite;

using Common.Client;
using Common.DB;


public class MDSQLiteOpenFactoryOptions : SQLOpenOptions
{
    public MDSQLiteOptions? SqliteOptions { get; set; }
}

public class MDSqliteDBOpenFactory : ISQLOpenFactory
{
    private readonly MDSQLiteOpenFactoryOptions options;

    public MDSqliteDBOpenFactory(MDSQLiteOpenFactoryOptions options)
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