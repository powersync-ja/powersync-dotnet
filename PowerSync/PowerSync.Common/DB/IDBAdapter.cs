namespace PowerSync.Common.DB;

using System.Collections.Generic;
using System.Threading.Tasks;

public class NonQueryResult
{
    // Represents the auto-generated row id if applicable.
    public long? InsertId { get; set; }

    // Number of affected rows.
    public int RowsAffected { get; set; }
}

public class QueryResult
{
    public List<IQueryRow> Rows { get; } = rows;
    public int Count => Rows.Count;
}

public interface IQueryRow
{
    public string GetString(string columnName);
    public int GetInt(string columnName);
    public long GetLong(string columnName);
    public bool GetBool(string columnName);
    public double GetDouble(string columnName);
    public DateTime GetDateTime(string columnName);

    public string? GetStringOrNull(string columnName);
    public int? GetIntOrNull(string columnName);
    public long? GetLongOrNull(string columnName);
    public bool? GetBoolOrNull(string columnName);
    public double? GetDoubleOrNull(string columnName);
    public DateTime? GetDateTimeOrNull(string columnName);
}

public interface IFromRow
{
    public T FromSql(QueryRow row);
}

public delegate T SqlMapper<T>(QueryRow row);

public interface IDBGetUtils
{
    // Execute a read-only query and return results.
    Task<QueryResult> GetAll(string sql, object?[]? parameters = null);
    Task<List<T>> GetAll<T>(string sql, object?[]? parameters = null, SqlMapper<T> mapper);
    Task<List<T>> GetAll<T>(string sql, object?[]? parameters = null) where T : IFromRow;

    // Execute a read-only query and return the first result, or null if the ResultSet is empty.
    Task<IQueryRow?> GetOptional(string sql, object?[]? parameters = null);
    Task<T?> GetOptional<T>(string sql, object?[]? parameters = null, SqlMapper<T> mapper);
    Task<T?> GetOptional<T>(string sql, object?[]? parameters = null) where T : IFromRow;

    // Execute a read-only query and return the first result, error if the ResultSet is empty.
    Task<IQueryRow> Get(string sql, object?[]? parameters = null);
    Task<T> Get<T>(string sql, object?[]? parameters = null, SqlMapper<T> mapper);
    Task<T> Get<T>(string sql, object?[]? parameters = null) where T : IFromRow;
}

public interface ILockContext : IDBGetUtils
{
    // Execute a single write statement.
    Task<NonQueryResult> Execute(string query, object?[]? parameters = null);

    // Execute a batch of write statements. 
    Task<NonQueryResult> ExecuteBatch(string query, object?[][]? parameters = null);
}

public interface ITransaction : ILockContext
{
    // Commit multiple changes to the local DB using the Transaction context.
    Task Commit();

    // Roll back multiple attempted changes using the Transaction context.
    Task Rollback();
}

public enum RowUpdateType
{
    SQLITE_INSERT = 18,
    SQLITE_DELETE = 9,
    SQLITE_UPDATE = 23
}

public class TableUpdateOperation(RowUpdateType OpType, long RowId)
{
    public RowUpdateType OpType { get; set; } = OpType;
    public long RowId { get; set; } = RowId;
}

public interface INotification;

public class UpdateNotification(string table, RowUpdateType OpType, long RowId) : TableUpdateOperation(OpType, RowId), INotification
{
    public string Table { get; set; } = table;
}

public class BatchedUpdateNotification : INotification
{
    public UpdateNotification[] RawUpdates { get; set; } = [];
    public string[] Tables { get; set; } = [];
    public Dictionary<string, TableUpdateOperation[]> GroupedUpdates { get; set; } = [];
}

public class DBAdapterEvents : EventManager
{
    public interface IDBAdapterEvent;

    public class TablesUpdatedEvent(INotification tablesUpdatedNotification) : IDBAdapterEvent
    {
        public INotification TablesUpdated { get; set; } = tablesUpdatedNotification;
    }

    public EventStream<TablesUpdatedEvent> OnTablesUpdated { get; } = new();

    public DBAdapterEvents()
    {
        Register(OnTablesUpdated);
    }
}

public class DBLockOptions
{
    // Optional timeout in milliseconds.
    public int? TimeoutMs { get; set; }
}

public class DBAdapterUtils
{
    public static string[] ExtractTableUpdates(INotification update)
    {
        return update switch
        {
            BatchedUpdateNotification batchedUpdate => batchedUpdate.Tables,
            UpdateNotification singleUpdate => [singleUpdate.Table],
            _ => throw new ArgumentException("Invalid update type", nameof(update))
        };
    }
}

public interface IDBAdapter : ILockContext, ICloseableAsync
{
    /// <summary>
    /// Closes the adapter. 
    /// </summary>
    new Task Close();

    /// <summary>
    /// The name of the adapter.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The event manager for the adapter.
    /// </summary>
    DBAdapterEvents Events { get; }

    /// <summary>
    /// Executes a read lock with the given function.
    /// </summary>
    Task<T> ReadLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null);

    /// <summary>
    /// Executes a read transaction with the given function.
    /// </summary>
    Task<T> ReadTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null);

    /// <summary>
    /// Executes a write lock with the given function.
    /// </summary>
    Task WriteLock(Func<ILockContext, Task> fn, DBLockOptions? options = null);
    /// <summary>
    /// Executes a write lock with the given function.
    /// </summary>
    ///
    /// <remarks>
    /// This is an overload of <see cref="WriteLock"/> that allows the function to return a result.
    /// </remarks>
    Task<T> WriteLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null);

    /// <summary>
    /// Executes a write transaction with the given function. 
    /// </summary>
    Task WriteTransaction(Func<ITransaction, Task> fn, DBLockOptions? options = null);
    /// <summary>
    /// Executes a write transaction with the given function. 
    /// </summary>
    /// 
    /// <remarks>
    /// This is an overload of <see cref="WriteTransaction"/> that allows the function to return a result.
    /// </remarks>
    Task<T> WriteTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null);

    /// <summary>
    /// This method refreshes the schema information across all connections. This is for advanced use cases, and should generally not be needed.
    /// </summary>
    Task RefreshSchema();
}
