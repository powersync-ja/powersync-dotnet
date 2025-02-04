namespace Common.MicrosoftDataSqlite;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Common.DB;
using Microsoft.Data.Sqlite;

public class MDSAdapter : IDBAdapter
{
    public string Name => throw new NotImplementedException();

    public MDSConnection? writeConnection;

    private readonly Task initialized;

    public MDSAdapter()
    {
        initialized = Init();
    }

    private async Task Init()
    {
        writeConnection = await OpenConnection("powersync.db");
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
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private void LoadExtension(SqliteConnection db)
    {
        string extensionPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../libpowersync");
        db.EnableExtensions(true);
        db.LoadExtension(extensionPath, "sqlite3_powersync_init");
    }

    public void Close()
    {
        throw new NotImplementedException();
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

    public async Task<T> Get<T>(string sql, params object[] parameters)
    {
        await initialized;
        return await writeConnection!.Get<T>(sql);
    }

    public Task<List<T>> GetAll<T>(string sql, params object[] parameters)
    {
        throw new NotImplementedException();
    }

    public async Task<T?> GetOptional<T>(string sql, params object[] parameters)
    {
        await initialized;
        return await writeConnection!.GetOptional<T>(sql);
    }

    public Task<T> ReadLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public Task<T> ReadTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public Task<T> WriteLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public async void Yoink()
    {

        await this.WriteTransaction(async (tx) =>
        {
            return await tx.Execute("INSERT INTO powersync_operations(op, data) VALUES (?, ?)",
               new object[] { "clear_remove_ops", "" });
        });
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
            try { await ctx.Rollback(); } catch { /* Ignore rollback errors */ }
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

    public Task<T> Get<T>(string sql, params object[] parameters)
    {
        return adapter.Get<T>(sql, parameters);
    }

    public Task<List<T>> GetAll<T>(string sql, params object[] parameters)
    {
        return adapter.GetAll<T>(sql, parameters);
    }

    public Task<T?> GetOptional<T>(string sql, params object[] parameters)
    {
        return adapter.GetOptional<T>(sql, parameters);
    }
}
