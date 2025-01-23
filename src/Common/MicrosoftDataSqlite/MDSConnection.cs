namespace Common.MicrosoftDataSqlite;

using System.Threading.Tasks;
using Common.DB;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;


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

    public async Task<T?> GetOptional<T>(string sql)
    {
        var result = await Execute(sql);

        // If there are no rows, return null
        if (result.Rows.Array.Count == 0)
        {
            return default;
        }

        var firstRow = result.Rows.Array[0];

        if (firstRow == null)
        {
            return default;
        }

        // TODO: Improve mapping errors for when the result fields don't match the target type.
        // TODO: This conversion may be a performance bottleneck, it's the easiest mechamisn for getting result typing.
        string json = JsonConvert.SerializeObject(firstRow);
        return JsonConvert.DeserializeObject<T>(json);

    }

    public async Task<T> Get<T>(string sql)
    {
        return await GetOptional<T>(sql) ?? throw new InvalidOperationException("Result set is empty");
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