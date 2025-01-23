namespace Common.MicrosoftDataSqlite;

using System.Threading.Tasks;
using Common.DB;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using SQLitePCL;

public class MDSConnectionOptions(SqliteConnection database)
{
    public SqliteConnection Database { get; set; } = database;
}

public class MDSConnection
{

    public SqliteConnection Db;
    private List<UpdateNotification> updateBuffer;
    public MDSConnection(MDSConnectionOptions options)
    {
        Db = options.Database;
        updateBuffer = [];

        raw.sqlite3_rollback_hook(Db.Handle, RollbackHook, IntPtr.Zero);
        raw.sqlite3_update_hook(Db.Handle, UpdateHook, IntPtr.Zero);
    }

    private void RollbackHook(object user_data)
    {
        // TODO: Implement rollback hook
        Console.WriteLine($"Rollback Hook");
        updateBuffer.Clear();
    }

    private void UpdateHook(object user_data, int type, utf8z database, utf8z table, long rowId)
    {
        var opType = type switch
        {
            18 => RowUpdateType.SQLITE_INSERT,
            9 => RowUpdateType.SQLITE_DELETE,
            23 => RowUpdateType.SQLITE_UPDATE,
            _ => throw new InvalidOperationException($"Unknown update type: {type}"),
        };
        updateBuffer.Add(new UpdateNotification(table.utf8_to_string(), opType, rowId));
    }

    public void FlushUpdates()
    {
        if (updateBuffer.Count == 0)
        {
            return;
        }

        // TODO: Implement update flush

        updateBuffer.Clear();
    }

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
                // TODO: What should we do with null values?
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