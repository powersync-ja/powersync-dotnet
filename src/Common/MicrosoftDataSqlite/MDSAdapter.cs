namespace Common.MicrosoftDataSqlite;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Common.DB;
using Microsoft.Data.Sqlite;

public class MDSAdapter : IDBAdapter
{
    public string Name => throw new NotImplementedException();

    public MDSConnection writeConnection;

    public MDSAdapter()
    {
        writeConnection = OpenConnection("powersync.db");
        Console.WriteLine("Opened connection");
    }

    protected MDSConnection OpenConnection(string dbFilename)
    {
        var db = openDatabase(dbFilename);
        loadExtension(db);

        return new MDSConnection(new MDSConnectionOptions(db));
    }

    private static SqliteConnection openDatabase(string dbFilename)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private void loadExtension(SqliteConnection db)
    {
        string extensionPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../libpowersync.dylib");
        db.EnableExtensions(true);
        db.LoadExtension(extensionPath);
    }

    public void Close()
    {
        throw new NotImplementedException();
    }

    public Task<DB.QueryResult> Execute(string query, object[]? parameters = null)
    {
        return writeConnection.Execute(query);
    }

    public Task<DB.QueryResult> ExecuteBatch(string query, object[][]? parameters = null)
    {
        throw new NotImplementedException();
    }

    public Task<T> Get<T>(string sql, params object[] parameters)
    {
        throw new NotImplementedException();
    }

    public Task<List<T>> GetAll<T>(string sql, params object[] parameters)
    {
        throw new NotImplementedException();
    }

    public Task<T?> GetOptional<T>(string sql, params object[] parameters)
    {
        throw new NotImplementedException();
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
