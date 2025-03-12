namespace PowerSync.Common.MDSQLite;

using System;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using Nito.AsyncEx;

using PowerSync.Common.DB;
using PowerSync.Common.Utils;

public class MDSQLiteAdapterOptions()
{
    public string Name { get; set; } = null!;

    public MDSQLiteOptions? SqliteOptions;

}

public class MDSQLiteAdapter : EventStream<DBAdapterEvent>, IDBAdapter
{
    public string Name => options.Name;

    public MDSQLiteConnection? writeConnection;
    public MDSQLiteConnection? readConnection;

    private readonly Task initialized;

    protected MDSQLiteAdapterOptions options;

    protected RequiredMDSQLiteOptions resolvedMDSQLiteOptions;
    private CancellationTokenSource? tablesUpdatedCts;

    private readonly AsyncLock writeMutex = new();
    private readonly AsyncLock readMutex = new();

    public MDSQLiteAdapter(MDSQLiteAdapterOptions options)
    {
        this.options = options;
        resolvedMDSQLiteOptions = resolveMDSQLiteOptions(options.SqliteOptions);
        initialized = Init();
    }

    private RequiredMDSQLiteOptions resolveMDSQLiteOptions(MDSQLiteOptions? options)
    {
        var defaults = RequiredMDSQLiteOptions.DEFAULT_SQLITE_OPTIONS;
        return new RequiredMDSQLiteOptions
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
        readConnection = await OpenConnection(options.Name);

        string[] baseStatements =
        [
            $"PRAGMA busy_timeout = {resolvedMDSQLiteOptions.LockTimeoutMs}",
            $"PRAGMA cache_size = -{resolvedMDSQLiteOptions.CacheSizeKb}",
            $"PRAGMA temp_store = {resolvedMDSQLiteOptions.TemporaryStorage}"
        ];

        string[] writeConnectionStatements =
        [
            .. baseStatements,
            $"PRAGMA journal_mode = {resolvedMDSQLiteOptions.JournalMode}",
            $"PRAGMA journal_size_limit = {resolvedMDSQLiteOptions.JournalSizeLimit}",
            $"PRAGMA synchronous = {resolvedMDSQLiteOptions.Synchronous}",
        ];

        string[] readConnectionStatements =
        [
            .. baseStatements,
            "PRAGMA query_only = true",
        ];

        foreach (var statement in writeConnectionStatements)
        {
            for (int tries = 0; tries < 30; tries++)
            {
                await writeConnection!.Execute(statement);
                tries = 30;
            }
        }

        foreach (var statement in readConnectionStatements)
        {
            await readConnection!.Execute(statement);
        }

        tablesUpdatedCts = new CancellationTokenSource();
        var _ = Task.Run(() =>
        {
            foreach (var notification in writeConnection!.Listen(tablesUpdatedCts.Token))
            {
                if (notification.TablesUpdated != null)
                {
                    Emit(notification);
                }
            }
        });
    }

    protected async Task<MDSQLiteConnection> OpenConnection(string dbFilename)
    {
        var db = OpenDatabase(dbFilename);
        LoadExtension(db);

        var connection = new MDSQLiteConnection(new MDSQLiteConnectionOptions(db));
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
        string extensionPath = PowerSyncPathResolver.GetNativeLibraryPath(AppContext.BaseDirectory);
        db.EnableExtensions(true);
        db.LoadExtension(extensionPath, "sqlite3_powersync_init");
    }

    public new void Close()
    {
        tablesUpdatedCts?.Cancel();
        base.Close();
        writeConnection?.Close();
        readConnection?.Close();
    }

    public async Task<NonQueryResult> Execute(string query, object[]? parameters = null)
    {
        return await WriteLock((ctx) => ctx.Execute(query, parameters));
    }

    public Task<QueryResult> ExecuteBatch(string query, object[][]? parameters = null)
    {
        // https://learn.microsoft.com/en-gb/dotnet/standard/data/sqlite/batching
        throw new NotImplementedException();
    }

    public async Task<T> Get<T>(string sql, params object[]? parameters)
    {
        return await ReadLock((ctx) => ctx.Get<T>(sql, parameters));
        ;
    }

    public async Task<T[]> GetAll<T>(string sql, params object[]? parameters)
    {
        return await ReadLock((ctx) => ctx.GetAll<T>(sql, parameters));
    }

    public async Task<T?> GetOptional<T>(string sql, params object[]? parameters)
    {
        return await ReadLock((ctx) => ctx.GetOptional<T>(sql, parameters));
    }

    public async Task<T> ReadTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null)
    {
        return await ReadLock((ctx) => InternalTransaction(new MDSQLiteTransaction(readConnection!)!, fn));
    }

    public async Task<T> ReadLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null)
    {
        await initialized;

        T result;
        using (await readMutex.LockAsync())
        {
            result = await fn(readConnection!);
        }

        return result;
    }

    public async Task WriteLock(Func<ILockContext, Task> fn, DBLockOptions? options = null)
    {
        await initialized;

        using (await writeMutex.LockAsync())
        {
            await fn(writeConnection!);
        }

        writeConnection!.FlushUpdates();

    }

    public async Task<T> WriteLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null)
    {
        await initialized;

        T result;
        using (await writeMutex.LockAsync())
        {
            result = await fn(writeConnection!);
        }

        writeConnection!.FlushUpdates();

        return result;
    }

    public async Task WriteTransaction(Func<ITransaction, Task> fn, DBLockOptions? options = null)
    {
        await WriteLock(ctx => InternalTransaction(new MDSQLiteTransaction(writeConnection!)!, fn));
    }

    public async Task<T> WriteTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null)
    {
        return await WriteLock((ctx) => InternalTransaction(new MDSQLiteTransaction(writeConnection!)!, fn));
    }

    protected static async Task InternalTransaction(
        MDSQLiteTransaction ctx,
        Func<ITransaction, Task> fn)
    {
        await RunTransaction(ctx, () => fn(ctx));
    }

    protected static async Task<T> InternalTransaction<T>(
        MDSQLiteTransaction ctx,
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
        MDSQLiteTransaction ctx,
        Func<Task> action)
    {
        try
        {
            await ctx.Begin();
            await action();
            await ctx.Commit();
        }
        catch (Exception)
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
        await readConnection!.RefreshSchema();
    }
}

public class MDSQLiteTransaction(MDSQLiteConnection connection) : ITransaction
{
    private readonly MDSQLiteConnection connection = connection;
    private bool finalized = false;

    public async Task Begin()
    {
        if (finalized) return;
        await connection.Execute("BEGIN");
    }

    public async Task Commit()
    {
        if (finalized) return;
        finalized = true;
        await connection.Execute("COMMIT");
    }

    public async Task Rollback()
    {
        if (finalized) return;
        finalized = true;
        await connection.Execute("ROLLBACK");
    }

    public Task<NonQueryResult> Execute(string query, object[]? parameters = null)
    {
        return connection.Execute(query, parameters);
    }

    public Task<T> Get<T>(string sql, params object[]? parameters)
    {
        return connection.Get<T>(sql, parameters);
    }

    public Task<T[]> GetAll<T>(string sql, params object[]? parameters)
    {
        return connection.GetAll<T>(sql, parameters);
    }

    public Task<T?> GetOptional<T>(string sql, params object[]? parameters)
    {
        return connection.GetOptional<T>(sql, parameters);
    }
}
