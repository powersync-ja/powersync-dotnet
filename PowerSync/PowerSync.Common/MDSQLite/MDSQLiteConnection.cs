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

    private static DynamicParameters? PrepareQuery(ref string query, object?[]? parameters)
    {
        if (parameters == null || parameters.Length == 0)
        {
            return null;
        }

        int parameterCount = parameters.Length;
        var parameterNames = PrepareQueryString(ref query, parameterCount);

        var dynamicParams = new DynamicParameters();

        for (int i = 0; i < parameterCount; i++)
        {
            dynamicParams.Add(parameterNames[i], parameters[i]);
        }

        return dynamicParams;
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

        var dynamicParamsList = new List<DynamicParameters>();

        foreach (var paramSet in parameters)
        {
            if (paramSet.Length != parameterCount)
            {
                throw new ArgumentException("Parameter sets have different number of arguments.");
            }

            var dynamicParams = new DynamicParameters();
            for (int i = 0; i < parameterCount; i++)
            {
                dynamicParams.Add(parameterNames[i], paramSet[i]);
            }
            dynamicParamsList.Add(dynamicParams);
        }

        return dynamicParamsList;
    }

    public async Task<T[]> GetAll<T>(string query, object?[]? parameters = null)
    {
        DynamicParameters? dynamicParams = PrepareQuery(ref query, parameters);
        return [..dynamicParams == null
            ? await Db.QueryAsync<T>(query, commandType: CommandType.Text)
            : await Db.QueryAsync<T>(query, dynamicParams, commandType: CommandType.Text)];
    }

    public async Task<dynamic[]> GetAll(string query, object?[]? parameters = null)
    {
        DynamicParameters? dynamicParams = PrepareQuery(ref query, parameters);
        return [..dynamicParams == null
            ? await Db.QueryAsync(query, commandType: CommandType.Text)
            : await Db.QueryAsync(query, dynamicParams, commandType: CommandType.Text)];
    }

    public async Task<T?> GetOptional<T>(string query, object?[]? parameters = null)
    {
        DynamicParameters? dynamicParams = PrepareQuery(ref query, parameters);
        return dynamicParams == null
            ? await Db.QueryFirstOrDefaultAsync<T>(query, commandType: CommandType.Text)
            : await Db.QueryFirstOrDefaultAsync<T>(query, dynamicParams, commandType: CommandType.Text);
    }

    public async Task<dynamic?> GetOptional(string query, object?[]? parameters = null)
    {
        DynamicParameters? dynamicParams = PrepareQuery(ref query, parameters);
        return dynamicParams == null
            ? await Db.QueryFirstOrDefaultAsync(query, commandType: CommandType.Text)
            : await Db.QueryFirstOrDefaultAsync(query, dynamicParams, commandType: CommandType.Text);
    }

    public async Task<T> Get<T>(string query, object?[]? parameters = null)
    {
        DynamicParameters? dynamicParams = PrepareQuery(ref query, parameters);
        return dynamicParams == null
            ? await Db.QueryFirstAsync<T>(query, commandType: CommandType.Text)
            : await Db.QueryFirstAsync<T>(query, dynamicParams, commandType: CommandType.Text);
    }

    public async Task<dynamic> Get(string query, object?[]? parameters = null)
    {
        DynamicParameters? dynamicParams = PrepareQuery(ref query, parameters);
        return dynamicParams == null
            ? await Db.QueryFirstAsync(query, commandType: CommandType.Text)
            : await Db.QueryFirstAsync(query, dynamicParams, commandType: CommandType.Text);
    }

    public async Task<NonQueryResult> Execute(string query, object?[]? parameters = null)
    {
        DynamicParameters? dynamicParams = PrepareQuery(ref query, parameters);
        int rowsAffected = dynamicParams == null
            ? await Db.ExecuteAsync(query, commandType: CommandType.Text)
            : await Db.ExecuteAsync(query, dynamicParams, commandType: CommandType.Text);

        return new NonQueryResult
        {
            InsertId = raw.sqlite3_last_insert_rowid(Db.Handle),
            RowsAffected = rowsAffected,
        };
    }

    public async Task<NonQueryResult> ExecuteBatch(string query, object?[][]? parameters = null)
    {
        if (parameters == null || parameters.Length == 0)
        {
            return new NonQueryResult { RowsAffected = 0 };
        }

        List<DynamicParameters>? dynamicParamsList = PrepareQuery(ref query, parameters);
        if (dynamicParamsList == null)
        {
            return new NonQueryResult { RowsAffected = 0 };
        }

        int rowsAffected = await Db.ExecuteAsync(query, dynamicParamsList, commandType: CommandType.Text);

        return new NonQueryResult
        {
            InsertId = raw.sqlite3_last_insert_rowid(Db.Handle),
            RowsAffected = rowsAffected,
        };
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
