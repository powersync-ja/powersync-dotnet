namespace PowerSync.Common.MDSQLite;

using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Newtonsoft.Json;

using PowerSync.Common.DB;
using PowerSync.Common.Utils;

using SQLitePCL;

public class MDSQLiteConnectionOptions(SqliteConnection database)
{
    public SqliteConnection Database { get; set; } = database;
}

public class MDSQLiteConnection : EventStream<DBAdapterEvent>, ILockContext
{

    public SqliteConnection Db;
    private readonly List<UpdateNotification> updateBuffer;
    public MDSQLiteConnection(MDSQLiteConnectionOptions options)
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

        var groupedUpdates = updateBuffer
       .GroupBy(update => update.Table)
       .ToDictionary(
           group => group.Key,
           group => group.Select(update => new TableUpdateOperation(update.OpType, update.RowId)).ToArray()
       );

        var batchedUpdate = new BatchedUpdateNotification
        {
            GroupedUpdates = groupedUpdates,
            RawUpdates = updateBuffer.ToArray(),
            Tables = groupedUpdates.Keys.ToArray()
        };

        updateBuffer.Clear();
        Emit(new DBAdapterEvent { TablesUpdated = batchedUpdate });
    }

    /// <summary>
    /// Replaces ? placeholders with named parameters and sets up the command.
    /// Returns the parameter names for reference.
    /// </summary>
    private static List<string> PrepareCommandParameters(SqliteCommand command, string query, int parameterCount)
    {
        var parameterNames = new List<string>();

        if (parameterCount == 0)
        {
            command.CommandText = query;
            return parameterNames;
        }

        // Count placeholders
        int placeholderCount = query.Count(c => c == '?');
        if (placeholderCount != parameterCount)
        {
            throw new ArgumentException($"Number of parameters ({parameterCount}) does not match the number of `?` placeholders ({placeholderCount}) in the query.");
        }

        // Replace `?` sequentially with named parameters
        for (int i = 0; i < parameterCount; i++)
        {
            string paramName = $"@param{i}";
            parameterNames.Add(paramName);

            int index = query.IndexOf('?');
            if (index == -1)
            {
                throw new ArgumentException("Mismatch between placeholders and parameters.");
            }

            query = string.Concat(query.Substring(0, index), paramName, query.Substring(index + 1));

        }

        command.CommandText = query;

        // Create empty parameter objects
        foreach (var paramName in parameterNames)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = paramName;
            command.Parameters.Add(parameter);
        }

        return parameterNames;
    }

    private static void PrepareCommand(SqliteCommand command, string query, object?[]? parameters)
    {
        int paramCount = parameters?.Length ?? 0;
        PrepareCommandParameters(command, query, paramCount);

        // Set the values
        if (parameters != null)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                command.Parameters[i].Value = parameters[i] ?? DBNull.Value;
            }
        }
    }


    public async Task<NonQueryResult> Execute(string query, object?[]? parameters = null)
    {
        using var command = Db.CreateCommand();
        PrepareCommand(command, query, parameters);

        int rowsAffected = await command.ExecuteNonQueryAsync();

        return new NonQueryResult
        {
            InsertId = raw.sqlite3_last_insert_rowid(Db.Handle),
            RowsAffected = rowsAffected
        };
    }

    public async Task<NonQueryResult> ExecuteBatch(string query, object?[][]? parameters = null)
    {
        parameters ??= [];

        if (parameters.Length == 0)
        {
            return new NonQueryResult { RowsAffected = 0 };
        }

        int totalRowsAffected = 0;

        var command = Db.CreateCommand();

        // Prepare command once with parameter placeholders
        int paramCount = parameters[0]?.Length ?? 0;
        PrepareCommandParameters(command, query, paramCount);

        // Execute for each parameter set (reuses compiled statement)
        foreach (var paramSet in parameters)
        {
            if (paramSet != null)
            {
                for (int i = 0; i < paramSet.Length; i++)
                {
                    command.Parameters[i].Value = paramSet[i] ?? DBNull.Value;
                }
            }

            totalRowsAffected += await command.ExecuteNonQueryAsync();
        }

        return new NonQueryResult
        {
            RowsAffected = totalRowsAffected,
            InsertId = raw.sqlite3_last_insert_rowid(Db.Handle)
        };
    }

    public async Task<QueryResult> ExecuteQuery(string query, object?[]? parameters = null)
    {
        var result = new QueryResult();
        using var command = Db.CreateCommand();
        PrepareCommand(command, query, parameters);

        var rows = new List<Dictionary<string, object>>();

        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null! : reader.GetValue(i);
            }
            rows.Add(row);
        }

        result.Rows.Array = rows;
        return result;
    }

    public async Task<T[]> GetAll<T>(string sql, object?[]? parameters = null)
    {
        var result = await ExecuteQuery(sql, parameters);

        // If there are no rows, return an empty array.
        if (result.Rows.Array.Count == 0)
        {
            return [];
        }

        var items = new List<T>();

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

    public async Task<T?> GetOptional<T>(string sql, object?[]? parameters = null)
    {
        var result = await ExecuteQuery(sql, parameters);

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

        string json = JsonConvert.SerializeObject(firstRow);
        return JsonConvert.DeserializeObject<T>(json);
    }

    public async Task<T> Get<T>(string sql, object?[]? parameters = null)
    {
        return await GetOptional<T>(sql, parameters) ?? throw new InvalidOperationException("Result set is empty");
    }

    public new void Close()
    {
        base.Close();
        Db.Close();
        // https://stackoverflow.com/questions/8511901/system-data-sqlite-close-not-releasing-database-file
        SqliteConnection.ClearPool(Db);
    }

    public async Task RefreshSchema()
    {
        await Get<object>("PRAGMA table_info('sqlite_master')");
    }
}
