using Common.Client;
using Common.DB;

namespace Common.MicrosoftDataSqlite;

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
        return new MDSAdapter(new MDSAdapterOptions
        {
            Name = options.DbFilename,
            SqliteOptions = options.SqliteOptions
        });
    }
}