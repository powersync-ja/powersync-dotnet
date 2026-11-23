namespace PowerSync.Common.DB;

using System.Collections.Generic;
using System.Threading.Tasks;

using PowerSync.Common.Utils;

public class NonQueryResult
{
    // Represents the auto-generated row id if applicable.
    public long? InsertId { get; set; }

    // Number of affected rows.
    public int RowsAffected { get; set; }
}

public class QueryResult(List<IQueryRow> rows)
{
    public List<IQueryRow> Rows { get; } = rows;
    public int Count => Rows.Count;
}

public interface IQueryRow
{
    public IReadOnlyList<string> Columns { get; }
    public object? GetValue(string columnName);

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

public interface IFromRow<TSelf> where TSelf : IFromRow<TSelf>
{
    /// <summary>
    /// Creates an instance from a result row. <paramref name="row"/> is only valid during this call.
    /// </summary>
    public static abstract TSelf FromRow(IQueryRow row);
}

/// <summary>
/// Maps a result row. <paramref name="row"/> is only valid during this call.
/// </summary>
public delegate T SqlMapper<T>(IQueryRow row);

public interface IDBGetUtils
{
    /// <summary>
    /// Executes a read-only query and returns all rows.
    /// </summary>
    Task<QueryResult> GetAll(string sql, object?[]? parameters = null);

    /// <summary>
    /// Executes a read-only query and returns all rows, mapped using <paramref name="mapper"/>.
    /// </summary>
    Task<List<T>> GetAll<T>(string sql, object?[]? parameters, SqlMapper<T> mapper);

    /// <summary>
    /// Executes a read-only query and returns all rows, mapped using <c>T.FromRow</c>.
    /// </summary>
    Task<List<T>> GetAll<T>(string sql, object?[]? parameters = null) where T : IFromRow<T>;

    /// <summary>
    /// Executes a read-only query and returns the first row, or null if there are no rows.
    /// </summary>
    Task<IQueryRow?> GetOptional(string sql, object?[]? parameters = null);

    /// <summary>
    /// Executes a read-only query and returns the first row mapped using <paramref name="mapper"/>,
    /// or <c>default(T)</c> if there are no rows.
    /// Note that <c>default(T)</c> is not null for all types.
    /// Map to a nullable type (e.g. <c>row =&gt; (long?)row.GetLong("id")</c>) to ensure you get null.
    /// </summary>
    Task<T?> GetOptional<T>(string sql, object?[]? parameters, SqlMapper<T> mapper);

    /// <summary>
    /// Executes a read-only query and returns the first row mapped using <c>T.FromRow</c>,
    /// or <c>default(T)</c> if there are no rows.
    /// Note that <c>default(T)</c> is not null if T is a value type.
    /// </summary>
    Task<T?> GetOptional<T>(string sql, object?[]? parameters = null) where T : IFromRow<T>;

    /// <summary>
    /// Executes a read-only query and returns the first row. Throws if there are no rows.
    /// </summary>
    Task<IQueryRow> Get(string sql, object?[]? parameters = null);

    /// <summary>
    /// Executes a read-only query and returns the first row mapped using <paramref name="mapper"/>.
    /// Throws if there are no rows.
    /// </summary>
    Task<T> Get<T>(string sql, object?[]? parameters, SqlMapper<T> mapper);

    /// <summary>
    /// Executes a read-only query and returns the first row mapped using <c>T.FromRow</c>.
    /// Throws if there are no rows.
    /// </summary>
    Task<T> Get<T>(string sql, object?[]? parameters = null) where T : IFromRow<T>;
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

public class DBAdapterEvents : EventManager
{
    public interface IDBAdapterEvent;

    public class TablesUpdatedEvent(HashSet<string> tablesUpdated) : IDBAdapterEvent
    {
        public HashSet<string> TablesUpdated { get; set; } = tablesUpdated;
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
