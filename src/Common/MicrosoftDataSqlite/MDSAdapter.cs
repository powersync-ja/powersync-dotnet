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
        return await writeConnection!.Execute(query);
    }

    public Task<DB.QueryResult> ExecuteBatch(string query, object[][]? parameters = null)
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

    public Task RefreshSchema()
    {
        throw new NotImplementedException();
    }

    public Task<T> WriteLock<T>(Func<ILockContext, Task<T>> fn, DBLockOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public Task<T> WriteTransaction<T>(Func<ITransaction, Task<T>> fn, DBLockOptions? options = null)
    {
        throw new NotImplementedException();
    }
}
