namespace PowerSync.Common.MDSQLite;

using System.Text;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using PowerSync.Common.DB;
using PowerSync.Common.Utils;

using SQLitePCL;

public class MDSQLiteConnectionOptions(SqliteConnection database)
{
    public SqliteConnection Database { get; set; } = database;
}

public class MDSQLiteConnection : EventStream<DBAdapterEvents.TablesUpdatedEvent>, ILockContext
{
    public SqliteConnection Db;
    private readonly List<UpdateNotification> _updateBuffer;
    private readonly object _updateBufferLock = new();
    public MDSQLiteConnection(MDSQLiteConnectionOptions options)
    {
        Db = options.Database;
        _updateBuffer = [];

        raw.sqlite3_rollback_hook(Db.Handle, RollbackHook, IntPtr.Zero);
        raw.sqlite3_update_hook(Db.Handle, UpdateHook, IntPtr.Zero);
    }

    private void RollbackHook(object user_data)
    {
        lock (_updateBufferLock)
        {
            _updateBuffer.Clear();
        }
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
        lock (_updateBufferLock)
        {
            _updateBuffer.Add(new UpdateNotification(table.utf8_to_string(), opType, rowId));
        }
    }

    public void FlushUpdates()
    {
        UpdateNotification[] updates;
        lock (_updateBufferLock)
        {
            if (_updateBuffer.Count == 0)
            {
                return;
            }
            updates = _updateBuffer.ToArray();
            _updateBuffer.Clear();
        }

        var groupedUpdates = updates
            .GroupBy(update => update.Table)
            .ToDictionary(
                group => group.Key,
                group => group.Select(update => new TableUpdateOperation(update.OpType, update.RowId)).ToArray()
            );

        var batchedUpdate = new BatchedUpdateNotification
        {
            GroupedUpdates = groupedUpdates,
            RawUpdates = updates.ToArray(),
            Tables = groupedUpdates.Keys.ToArray()
        };

        Emit(new DBAdapterEvents.TablesUpdatedEvent(batchedUpdate));
    }

    private static List<string> PrepareQueryString(ref string query, int parameterCount)
    {
        var parameterList = new List<string>();
        if (parameterCount == 0)
        {
            return parameterList;
        }

        // Replace `?` sequentially with named parameters
        var sb = new StringBuilder(query.Length + parameterCount * 7);
        int lastPos = 0;
        int currentPos;
        for (int i = 0; i < parameterCount; i++)
        {
            currentPos = query.IndexOf('?', lastPos);
            if (currentPos == -1)
                throw new ArgumentException($"Not enough `?` placeholders for {parameterCount} parameters.");

            string paramName = $"@param{i}";
            parameterList.Add(paramName);

            sb.Append(query, lastPos, currentPos - lastPos);
            sb.Append(paramName);

            lastPos = currentPos + 1;
        }

        // Append any remaining chars
        if (lastPos < query.Length)
        {
            sb.Append(query, lastPos, query.Length - lastPos);
        }

        query = sb.ToString();

        return parameterList;
    }

    private static void PrepareCommandParameters(SqliteCommand command, ref string query, int parameterCount)
    {
        var parameterNames = PrepareQueryString(ref query, parameterCount);
        command.CommandText = query;
        foreach (var paramName in parameterNames)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = paramName;
            command.Parameters.Add(parameter);
        }
    }

    private static void PrepareCommand(SqliteCommand command, ref string query, object?[]? parameters)
    {
        int parameterCount = parameters?.Length ?? 0;
        PrepareCommandParameters(command, ref query, parameterCount);

        if (parameters != null)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                command.Parameters[i].Value = parameters[i] ?? DBNull.Value;
            }
        }
    }

    private QueryResult ReadRows(string query, object?[]? parameters, int? limit = null)
    {
        using var command = Db.CreateCommand();
        PrepareCommand(command, ref query, parameters);

        using var reader = command.ExecuteReader();
        var columns = new string[reader.FieldCount];
        var columnIndices = new Dictionary<string, int>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns[i] = reader.GetName(i);
            columnIndices.TryAdd(columns[i], i);
        }

        var rows = new List<IQueryRow>();
        while ((limit == null || rows.Count < limit) && reader.Read())
        {
            var values = new object?[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(new QueryRow(columns, columnIndices, values));
        }

        return new QueryResult(rows);
    }

    private IQueryRow? ReadFirstRow(string query, object?[]? parameters)
    {
        var result = ReadRows(query, parameters, limit: 1);
        return result.Count > 0 ? result.Rows[0] : null;
    }

    public Task<QueryResult> GetAll(string query, object?[]? parameters = null)
    {
        return Task.Run(() => ReadRows(query, parameters));
    }

    public Task<List<T>> GetAll<T>(string query, object?[]? parameters, SqlMapper<T> mapper)
    {
        return Task.Run(() => ReadRows(query, parameters).Rows.Select(row => mapper(row)).ToList());
    }

    public Task<List<T>> GetAll<T>(string query, object?[]? parameters = null) where T : IFromRow<T>
    {
        return GetAll(query, parameters, T.FromRow);
    }

    public Task<IQueryRow?> GetOptional(string query, object?[]? parameters = null)
    {
        return Task.Run(() => ReadFirstRow(query, parameters));
    }

    public Task<T?> GetOptional<T>(string query, object?[]? parameters, SqlMapper<T> mapper)
    {
        return Task.Run(() => ReadFirstRow(query, parameters) is { } row ? mapper(row) : default);
    }

    public Task<T?> GetOptional<T>(string query, object?[]? parameters = null) where T : IFromRow<T>
    {
        return GetOptional(query, parameters, T.FromRow);
    }

    public Task<IQueryRow> Get(string query, object?[]? parameters = null)
    {
        return Task.Run(() => ReadFirstRow(query, parameters) ?? throw new InvalidOperationException("Query returned no rows."));
    }

    public Task<T> Get<T>(string query, object?[]? parameters, SqlMapper<T> mapper)
    {
        return Task.Run(() => mapper(ReadFirstRow(query, parameters) ?? throw new InvalidOperationException("Query returned no rows.")));
    }

    public Task<T> Get<T>(string query, object?[]? parameters = null) where T : IFromRow<T>
    {
        return Get(query, parameters, T.FromRow);
    }

    public Task<NonQueryResult> Execute(string query, object?[]? parameters = null) => Task.Run(() =>
    {
        using var command = Db.CreateCommand();
        PrepareCommand(command, ref query, parameters);

        int rowsAffected = command.ExecuteNonQuery();
        return new NonQueryResult
        {
            InsertId = raw.sqlite3_last_insert_rowid(Db.Handle),
            RowsAffected = rowsAffected,
        };
    });

    public Task<NonQueryResult> ExecuteBatch(string query, object?[][]? parameters = null)
    {
        if (parameters == null || parameters.Length == 0)
        {
            return Task.FromResult(new NonQueryResult { RowsAffected = 0 });
        }

        int parameterCount = parameters[0].Length;
        if (parameterCount == 0)
        {
            return Task.FromResult(new NonQueryResult { RowsAffected = 0 });
        }

        return Task.Run(() =>
        {
            int totalRowsAffected = 0;

            using var command = Db.CreateCommand();
            PrepareCommandParameters(command, ref query, parameterCount);

            foreach (var paramSet in parameters)
            {
                if (paramSet != null)
                {
                    for (int i = 0; i < paramSet.Length; i++)
                    {
                        command.Parameters[i].Value = paramSet[i] ?? DBNull.Value;
                    }
                }

                totalRowsAffected += command.ExecuteNonQuery();
            }

            return new NonQueryResult
            {
                InsertId = raw.sqlite3_last_insert_rowid(Db.Handle),
                RowsAffected = totalRowsAffected,
            };
        });
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
        await Get("PRAGMA table_info('sqlite_master')");
    }
}
