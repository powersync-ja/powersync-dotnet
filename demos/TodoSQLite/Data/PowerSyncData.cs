using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using Newtonsoft.Json;
using PowerSync.Common.Client;
using PowerSync.Common.MDSQLite;
using PowerSync.Maui.SQLite;
using SQLite;
using TodoSQLite.Models;

namespace TodoSQLite.Data;

public class PowerSyncData
{
    private PowerSyncDatabase _db;
    private ILogger _logger;

    private record ListResult(string id, string name, string owner_id, string created_at);
    private record TodoResult(string id, string list_id, string description, string created_at, string completed_at, string created_by, string completed_by, int completed);

    async Task Init()
    {
        if (_db != null) return;
        
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Trace);
        });
        _logger = loggerFactory.CreateLogger("PowerSyncLogger");
        
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "mydb.db");
        var factory = new MAUISQLiteDBOpenFactory(new MDSQLiteOpenFactoryOptions()
        {
            DbFilename = dbPath
        });
        _db = new PowerSyncDatabase(new PowerSyncDatabaseOptions()
        {
            Database = factory,
            Schema = AppSchema.PowerSyncSchema,
            Logger = _logger
        });
        await _db.Init();

        // var nodeConnector = new NodeConnector();
        // await _db.Connect(nodeConnector);
    }

    // List operations
    public async Task<List<TodoList>> GetListsAsync()
    {
        await Init();
        var results = await _db.GetAll<ListResult>("SELECT * FROM lists ORDER BY created_at DESC");
        return results.Select(r => new TodoList
        {
            ID = r.id,
            Name = r.name,
            OwnerId = r.owner_id,
            CreatedAt = r.created_at
        }).ToList();
    }

    public async Task<TodoList> GetListAsync(string id)
    {
        await Init();
        var result = await _db.GetAll<ListResult>("SELECT * FROM lists WHERE id = ?", [id.ToString()]);
        var list = result.FirstOrDefault();
        if (list == null) return null;

        return new TodoList
        {
            ID = list.id,
            Name = list.name,
            OwnerId = list.owner_id,
            CreatedAt = list.created_at
        };
    }

    public async Task<int> SaveListAsync(TodoList list)
    {
        await Init();
        if (list.ID != "")
        {
            await _db.Execute(
                "UPDATE lists SET name = ?, owner_id = ? WHERE id = ?",
                [list.Name, list.OwnerId, list.ID.ToString()]);
            return 1;
        }
        else
        {
            await _db.Execute(
                "INSERT INTO lists (id, name, owner_id, created_at) VALUES (uuid(), ?, ?, ?)",
                [list.Name, list.OwnerId, DateTime.UtcNow.ToString("o")]);
            return 1;
        }
    }

    public async Task<int> DeleteListAsync(TodoList list)
    {
        await Init();
        string listId = list.ID.ToString();
        // First delete all todo items in this list
        await _db.Execute("DELETE FROM todos WHERE list_id = ?", [listId]);
        await _db.Execute("DELETE FROM lists WHERE id = ?", [listId]);
        return 1;
    }

    // Todo item operations
    public async Task<List<TodoItem>> GetItemsAsync(string listId)
    {
        await Init();
        var results = await _db.GetAll<TodoResult>(
            "SELECT * FROM todos WHERE list_id = ? ORDER BY created_at DESC", [listId]);

        return results.Select(r => new TodoItem
        {
            ID = r.id,
            ListId = r.list_id,
            Description = r.description,
            CreatedAt = r.created_at,
            CompletedAt = r.completed_at,
            CreatedBy = r.created_by,
            CompletedBy = r.completed_by,
            Completed = r.completed == 1
        }).ToList();
    }

    public async Task<List<TodoItem>> GetItemsNotDoneAsync(string listId)
    {
        await Init();
        var results = await _db.GetAll<TodoResult>(
            "SELECT * FROM todos WHERE list_id = ? AND completed = 0 ORDER BY created_at DESC",
            [listId]);

        return results.Select(r => new TodoItem
        {
            ID = r.id,
            ListId = r.list_id,
            Description = r.description,
            CreatedAt = r.created_at,
            CompletedAt = r.completed_at,
            CreatedBy = r.created_by,
            CompletedBy = r.completed_by,
            Completed = r.completed == 1
        }).ToList();
    }

    public async Task<TodoItem> GetItemAsync(string id)
    {
        await Init();
        var results = await _db.GetAll<TodoResult>("SELECT * FROM todos WHERE id = ?", [id.ToString()]);
        var todo = results.FirstOrDefault();
        if (todo == null) return null;

        return new TodoItem
        {
            ID = todo.id,
            ListId = todo.list_id,
            Description = todo.description,
            CreatedAt = todo.created_at,
            CompletedAt = todo.completed_at,
            CreatedBy = todo.created_by,
            CompletedBy = todo.completed_by,
            Completed = todo.completed == 1
        };
    }

    public async Task<int> SaveItemAsync(TodoItem item)
    {
        await Init();
        if (item.ID != "")
        {
            await _db.Execute(
                @"UPDATE todos 
                  SET description = ?, completed = ?, completed_at = ?, completed_by = ?
                  WHERE id = ?",
                [item.Description,
                item.Completed ? 1 : 0,
                item.CompletedAt,
                item.CompletedBy,
                item.ID.ToString()]);
            return 1;
        }
        else
        {
            await _db.Execute(
                @"INSERT INTO todos 
                  (id, list_id, description, created_at, completed, created_by)
                  VALUES (uuid(), ?, ?, ?, ?, ?)",
                [item.ListId,
                item.Description,
                item.CreatedAt,
                item.Completed ? 1 : 0,
                item.CreatedBy]);
            return 1;
        }
    }

    public async Task<int> DeleteItemAsync(TodoItem item)
    {
        await Init();
        await _db.Execute("DELETE FROM todos WHERE id = ?", [item.ID.ToString()]);
        return 1;
    }
}