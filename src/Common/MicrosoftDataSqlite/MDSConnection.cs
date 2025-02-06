namespace Common.MicrosoftDataSqlite;

using System.Reflection.PortableExecutable;
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

    public async Task<QueryResult> Execute(string query, object[]? parameters = null)
    {
        var result = new QueryResult();

        using var command = Db.CreateCommand();

        if (parameters != null && parameters.Length > 0)
        {
            var parameterNames = new List<string>();

            // Count placeholders
            int placeholderCount = query.Count(c => c == '?');
            if (placeholderCount != parameters.Length)
            {
                throw new ArgumentException("Number of provided parameters does not match the number of `?` placeholders in the query.");
            }

            // Replace `?` sequentially with named parameters
            for (int i = 0; i < parameters.Length; i++)
            {
                string paramName = $"@param{i}";
                parameterNames.Add(paramName);

                // Replace only the first occurrence of `?`
                int index = query.IndexOf('?');
                if (index == -1)
                {
                    throw new ArgumentException("Mismatch between placeholders and parameters.");
                }

                query = string.Concat(query.AsSpan(0, index), paramName, query.AsSpan()[(index + 1)..]);
            }

            command.CommandText = query;

            // Add parameters to the command
            for (int i = 0; i < parameters.Length; i++)
            {
                command.Parameters.AddWithValue(parameterNames[i], parameters[i]);
            }
        }
        else
        {
            command.CommandText = query;
        }

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

        result.InsertId = raw.sqlite3_last_insert_rowid(Db.Handle);
        result.RowsAffected = reader.RecordsAffected;
        result.Rows.Array = rows;
        return result;
    }

    public async Task<T[]> GetAll<T>(string sql, object[]? parameters = null)
    {
        var result = await Execute(sql, parameters);

        // If there are no rows, return an empty array.
        if (result.Rows.Array.Count == 0)
        {
            return [];
        }

        var items = new List<T>();

        // TODO: Improve mapping errors for when the result fields don't match the target type.
        // TODO: This conversion may be a performance bottleneck, it's the easiest mechamisn for getting result typing.
        foreach (var row in result.Rows.Array)
        {
            if (row != null)
            {
                // Serialize the row to JSON and then deserialize it into type T.
                string json = JsonConvert.SerializeObject(row);
                T item = JsonConvert.DeserializeObject<T>(json)!;
                items.Add(item);
            }
        }

        return [.. items];
    }

    public async Task<T?> GetOptional<T>(string sql, object[]? parameters = null)
    {
        var result = await Execute(sql, parameters);

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

    public async Task<T> Get<T>(string sql, object[]? parameters = null)
    {
        return await GetOptional<T>(sql, parameters) ?? throw new InvalidOperationException("Result set is empty");
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