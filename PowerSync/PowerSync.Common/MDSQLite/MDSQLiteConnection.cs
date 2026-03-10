namespace PowerSync.Common.MDSQLite;

using System.Data;
using System.Text;
using System.Threading.Tasks;

using Dapper;

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
        Emit(new DBAdapterEvents.TablesUpdatedEvent(batchedUpdate));
    }

    private static List<string> PrepareQueryString(ref string query, int parameterCount)
    {
        var parameterList = new List<string>();
        if (parameterCount == 0)
        {
            return parameterList;
        }

        int placeholderCount = query.Count(c => c == '?');
        if (placeholderCount != parameterCount)
        {
            throw new ArgumentException($"Number of parameters ({parameterCount}) does not match the number of `?` placeholders ({placeholderCount}) in the query.");
        }

        // Replace `?` sequentially with named parameters
        var sb = new StringBuilder();
        int lastPos = 0;
        int currentPos;
        for (int i = 0; i < parameterCount; i++)
        {
            currentPos = query.IndexOf('?', lastPos);

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

    private static Dictionary<string, object?>? PrepareQuery(ref string query, object?[]? parameters)
    {
        if (parameters == null || parameters.Length == 0)
        {
            return null;
        }

        int parameterCount = parameters.Length;
        var parameterNames = PrepareQueryString(ref query, parameterCount);

        var paramDict = new Dictionary<string, object?>(parameterCount);

        for (int i = 0; i < parameterCount; i++)
        {
            paramDict[parameterNames[i]] = parameters[i];
        }

        return paramDict;
    }

    private static void PrepareCommandParameters(SqliteCommand command, ref string query, int parameterCount)
    {
        if (parameterCount == 0)
        {
            command.CommandText = query;
            return;
        }

        int placeholderCount = query.Count(c => c == '?');
        if (placeholderCount != parameterCount)
        {
            throw new ArgumentException($"Number of parameters ({parameterCount}) does not match the number of `?` placeholders ({placeholderCount}) in the query.");
        }

        var sb = new StringBuilder();
        int lastPos = 0;
        int currentPos;
        for (int i = 0; i < parameterCount; i++)
        {
            currentPos = query.IndexOf('?', lastPos);

            string paramName = $"@param{i}";

            sb.Append(query, lastPos, currentPos - lastPos);
            sb.Append(paramName);

            var parameter = command.CreateParameter();
            parameter.ParameterName = paramName;
            command.Parameters.Add(parameter);

            lastPos = currentPos + 1;
        }

        if (lastPos < query.Length)
        {
            sb.Append(query, lastPos, query.Length - lastPos);
        }

        command.CommandText = sb.ToString();
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

    private static List<DynamicParameters>? PrepareQuery(ref string query, object?[][]? parameters)
    {
        if (parameters == null || parameters.Length == 0)
        {
            return null;
        }

        var parameterCount = parameters[0].Length;
        if (parameterCount == 0)
        {
            return null;
        }

        var parameterNames = PrepareQueryString(ref query, parameterCount);

        var preparedParamsList = new List<DynamicParameters>();

        foreach (var paramSet in parameters)
        {
            if (paramSet.Length != parameterCount)
            {
                throw new ArgumentException("Parameter sets have different number of arguments.");
            }

            var preparedParams = new DynamicParameters();
            for (int i = 0; i < parameterCount; i++)
            {
                preparedParams.Add(parameterNames[i], paramSet[i]);
            }
            preparedParamsList.Add(preparedParams);
        }

        return preparedParamsList;
    }

    public Task<T[]> GetAll<T>(string query, object?[]? parameters = null)
    {
        var preparedParams = PrepareQuery(ref query, parameters);
        return Task.Run(async () => (await Db.QueryAsync<T>(query, preparedParams, commandType: CommandType.Text)).ToArray());
    }

    public Task<dynamic[]> GetAll(string query, object?[]? parameters = null)
    {
        var preparedParams = PrepareQuery(ref query, parameters);
        return Task.Run(async () => (await Db.QueryAsync(query, preparedParams, commandType: CommandType.Text)).ToArray());
    }

    public Task<T?> GetOptional<T>(string query, object?[]? parameters = null)
    {
        var preparedParams = PrepareQuery(ref query, parameters);
        return Task.Run(() => Db.QueryFirstOrDefaultAsync<T>(query, preparedParams, commandType: CommandType.Text));
    }

    public Task<dynamic?> GetOptional(string query, object?[]? parameters = null)
    {
        var preparedParams = PrepareQuery(ref query, parameters);
        return Task.Run(() => Db.QueryFirstOrDefaultAsync(query, preparedParams, commandType: CommandType.Text));
    }

    public Task<T> Get<T>(string query, object?[]? parameters = null)
    {
        var preparedParams = PrepareQuery(ref query, parameters);
        return Task.Run(() => Db.QueryFirstAsync<T>(query, preparedParams, commandType: CommandType.Text));
    }

    public Task<dynamic> Get(string query, object?[]? parameters = null)
    {
        var preparedParams = PrepareQuery(ref query, parameters);
        return Task.Run(() => Db.QueryFirstAsync(query, preparedParams, commandType: CommandType.Text));
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
        await Get<object>("PRAGMA table_info('sqlite_master')");
    }
}
