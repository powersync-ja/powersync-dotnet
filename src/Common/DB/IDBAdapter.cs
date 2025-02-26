namespace Common.DB;

using System.Collections.Generic;
using System.Threading.Tasks;
using Common.Utils;



public class NonQueryResult
{
    // Represents the auto-generated row id if applicable.
    public long? InsertId { get; set; }

    // Number of affected rows.
    public int RowsAffected { get; set; }
}

public class QueryResult
{
    public class QueryRows
    {
        // Raw array with all dataset.
        public List<Dictionary<string, object>> Array { get; set; } = [];

        // The length of the dataset.
        public int Length => Array.Count;
    }
    public QueryRows Rows { get; set; } = new QueryRows();
}

public interface IDBGetUtils
{
    // Execute a read-only query and return results.
    Task<T[]> GetAll<T>(string sql, params object[]? parameters);

    // Execute a read-only query and return the first result, or null if the ResultSet is empty.
    Task<T?> GetOptional<T>(string sql, params object[]? parameters);

    // Execute a read-only query and return the first result, error if the ResultSet is empty.
    Task<T> Get<T>(string sql, params object[]? parameters);
}

public interface ILockContext : IDBGetUtils
{
    // Execute a single write statement.
    Task<NonQueryResult> Execute(string query, object[]? parameters = null);
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

public interface INotification
{
}

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

public class DBAdapterEvent
{
    public INotification? TablesUpdated { get; set; }
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

// TODO remove IDBGetUtils - inheriting from ILockContext
public interface IDBAdapter : IEventStream<DBAdapterEvent>, IDBGetUtils, ILockContext
{

    // Closes the adapter.
    new void Close();

    // Execute a batch of write statements.
    Task<QueryResult> ExecuteBatch(string query, object[][]? parameters = null);

    // The name of the adapter.
    string Name { get; }

    // Executes a read lock with the given function.
    Task<T> ReadLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null);

    // Executes a read transaction with the given function.
    Task<T> ReadTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null);

    // Executes a write lock with the given function.
    Task WriteLock(Func<ILockContext, Task> fn, DBLockOptions? options = null);
    Task<T> WriteLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null);

    // Executes a write transaction with the given function. 
    Task WriteTransaction(Func<ITransaction, Task> fn, DBLockOptions? options = null);
    Task<T> WriteTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null);

    // This method refreshes the schema information across all connections. This is for advanced use cases, and should generally not be needed.
    Task RefreshSchema();
}
