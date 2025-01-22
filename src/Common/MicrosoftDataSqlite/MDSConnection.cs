namespace Common.MicrosoftDataSqlite;

using System.Threading.Tasks;
using Common.DB;
using Microsoft.Data.Sqlite;


public class MDSConnectionOptions(SqliteConnection database)
{
    public SqliteConnection Database { get; set; } = database;
}

public class MDSConnection(MDSConnectionOptions options)
{
    protected SqliteConnection Db { get; set; } = options.Database;

    public async Task<QueryResult> Execute(string query)
    {
        var result = new QueryResult();

        using var command = Db.CreateCommand();
        command.CommandText = query;
        var rows = new List<Dictionary<string, object>>();

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }

        // insertId = await db.Execute("SELECT last_insert_rowid();");
        result.RowsAffected = reader.RecordsAffected;
        result.Rows.Array = rows;
        return result;
    }

    public void Close()
    {
        Db.Close();
    }

    public async Task RefreshSchema()
    {
        await Execute("PRAGMA table_info('sqlite_master')");
    }
}