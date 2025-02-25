namespace Common.MicrosoftDataSqlite;

using System;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Common.DB;
using Common.Utils;

public class MDSAdapterOptions()
{
    public string Name { get; set; } = null!;

    public SqliteOptions? SqliteOptions;

}

public class MDSAdapter : EventStream<DBAdapterEvent>, IDBAdapter
{
    public string Name => throw new NotImplementedException();

    public MDSConnection? writeConnection;

    private readonly Task initialized;

    protected MDSAdapterOptions options;

    protected RequiredSqliteOptions resolvedSqliteOptions;
    private CancellationTokenSource? tablesUpdatedCts;

    public MDSAdapter(MDSAdapterOptions options)
    {
        this.options = options;
        resolvedSqliteOptions = resolveSqliteOptions(options.SqliteOptions);
        initialized = Init();
    }

    private RequiredSqliteOptions resolveSqliteOptions(SqliteOptions? options)
    {
        var defaults = RequiredSqliteOptions.DEFAULT_SQLITE_OPTIONS;
        return new RequiredSqliteOptions
        {
            JournalMode = options?.JournalMode ?? defaults.JournalMode,
            Synchronous = options?.Synchronous ?? defaults.Synchronous,
            JournalSizeLimit = options?.JournalSizeLimit ?? defaults.JournalSizeLimit,
            CacheSizeKb = options?.CacheSizeKb ?? defaults.CacheSizeKb,
            TemporaryStorage = options?.TemporaryStorage ?? defaults.TemporaryStorage,
            LockTimeoutMs = options?.LockTimeoutMs ?? defaults.LockTimeoutMs,
            EncryptionKey = options?.EncryptionKey ?? defaults.EncryptionKey,
            Extensions = options?.Extensions ?? defaults.Extensions
        };
    }

    private async Task Init()
    {
        writeConnection = await OpenConnection(options.Name);

        string[] baseStatements =
        [
            $"PRAGMA busy_timeout = {resolvedSqliteOptions.LockTimeoutMs}",
            $"PRAGMA cache_size = -{resolvedSqliteOptions.CacheSizeKb}",
            $"PRAGMA temp_store = {resolvedSqliteOptions.TemporaryStorage}"
        ];

        string[] writeConnectionStatements =
        [
            .. baseStatements,
            $"PRAGMA journal_mode = {resolvedSqliteOptions.JournalMode}",
            $"PRAGMA journal_size_limit = {resolvedSqliteOptions.JournalSizeLimit}",
            $"PRAGMA synchronous = {resolvedSqliteOptions.Synchronous}",
        ];

        foreach (var statement in writeConnectionStatements)
        {
            for (int tries = 0; tries < 30; tries++)
            {
                try
                {
                    await writeConnection!.Execute(statement);
                    tries = 30;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    throw;
                }
            }
        }

        tablesUpdatedCts = new CancellationTokenSource();
        var _ = Task.Run(() =>
        {
            foreach (var notification in Listen(tablesUpdatedCts.Token))
            {
                if (notification.TablesUpdated != null)
                {
                    Emit(notification);
                }
            }
        });
    }

    protected async Task<MDSConnection> OpenConnection(string dbFilename)
    {
        var db = OpenDatabase(dbFilename);
        LoadExtension(db);

        var connection = new MDSConnection(new MDSConnectionOptions(db));
        await connection.Execute("SELECT powersync_init()");

        return connection;
    }

    private static SqliteConnection OpenDatabase(string dbFilename)
    {
        var connection = new SqliteConnection($"Data Source={dbFilename}");
        connection.Open();
        return connection;
    }

    private void LoadExtension(SqliteConnection db)
    {
        string extensionPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../libpowersync");
        db.EnableExtensions(true);
        db.LoadExtension(extensionPath, "sqlite3_powersync_init");
    }

    public new void Close()
    {
        tablesUpdatedCts?.Cancel();
        base.Close();
        writeConnection?.Close();
    }

    public async Task<QueryResult> Execute(string query, object[]? parameters = null)
    {
        await initialized;
        return await writeConnection!.Execute(query, parameters);
    }

    public Task<QueryResult> ExecuteBatch(string query, object[][]? parameters = null)
    {
        throw new NotImplementedException();
    }

    public async Task<T> Get<T>(string sql, params object[]? parameters)
    {
        await initialized;
        return await writeConnection!.Get<T>(sql, parameters);
    }

    public async Task<T[]> GetAll<T>(string sql, params object[]? parameters)
    {
        await initialized;
        return await writeConnection!.GetAll<T>(sql, parameters);
    }

    public async Task<T?> GetOptional<T>(string sql, params object[]? parameters)
    {
        await initialized;
        return await writeConnection!.GetOptional<T>(sql, parameters);
    }

    public Task<T> ReadLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public async Task<T> ReadTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null)
    {
        return await InternalTransaction(new MDSTransaction(this)!, fn);
    }

    public Task<T> WriteLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null)
    {
        //     return new Promise(async (resolve, reject) => {
        //   try {
        //     await this.locks
        //       .acquire(
        //         LockType.WRITE,
        //         async () => {
        //           resolve(await fn(this.writeConnection!));
        //         },
        //         { timeout: options?.timeoutMs }
        //       )
        //       .then(() => {
        //         // flush updates once a write lock has been released
        //         this.writeConnection!.flushUpdates();
        //       });
        //   } catch (ex) {
        //     reject(ex);
        //   }
        // });
        throw new NotImplementedException();
    }

    public async Task WriteTransaction(Func<ITransaction, Task> fn, DBLockOptions? options = null)
    {
        await InternalTransaction(new MDSTransaction(this)!, fn);
    }
    public async Task<T> WriteTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null)
    {
        return await InternalTransaction(new MDSTransaction(this)!, fn);
    }

    protected static Task InternalTransaction(
        MDSTransaction ctx,
        Func<ITransaction, Task> fn)
    {
        return RunTransaction(ctx, () => fn(ctx));
    }

    protected static async Task<T> InternalTransaction<T>(
        MDSTransaction ctx,
        Func<ITransaction, Task<T>> fn)
    {
        T result = default!;
        await RunTransaction(ctx, async () =>
        {
            result = await fn(ctx);
        });
        return result;
    }

    private static async Task RunTransaction(
        ITransaction ctx,
        Func<Task> action)
    {
        try
        {
            await ctx.Execute("BEGIN");
            await action();
            await ctx.Commit();
        }
        catch
        {
            // In rare cases, a rollback may fail. Safe to ignore.
            try { await ctx.Rollback(); }
            catch
            {
                // Ignore rollback errors
            }
            throw;
        }
    }

    public async Task RefreshSchema()
    {
        await initialized;
        await writeConnection!.RefreshSchema();
    }
}

// TODO CL this could been a lock context
public class MDSTransaction(MDSAdapter adapter) : ITransaction
{

    private readonly MDSAdapter adapter = adapter;
    private bool finalized = false;

    public async Task Commit()
    {
        if (finalized) return;
        finalized = true;
        await adapter.Execute("COMMIT");
    }

    public async Task Rollback()
    {
        if (finalized) return;
        finalized = true;
        await adapter.Execute("ROLLBACK");
    }

    public Task<QueryResult> Execute(string query, object[]? parameters = null)
    {
        return adapter.Execute(query, parameters);
    }

    public Task<T> Get<T>(string sql, params object[]? parameters)
    {
        return adapter.Get<T>(sql, parameters);
    }

    public Task<T[]> GetAll<T>(string sql, params object[]? parameters)
    {
        return adapter.GetAll<T>(sql, parameters);
    }

    public Task<T?> GetOptional<T>(string sql, params object[]? parameters)
    {
        return adapter.GetOptional<T>(sql, parameters);
    }
}
