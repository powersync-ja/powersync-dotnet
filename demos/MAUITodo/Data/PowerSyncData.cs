using MAUITodo.Models;

using Microsoft.Extensions.Logging;

using PowerSync.Common.Client;
using PowerSync.Common.MDSQLite;
using PowerSync.Maui.SQLite;

namespace MAUITodo.Data;

public class PowerSyncData
{
    public PowerSyncDatabase Db;
    private string UserId { get; }

    public PowerSyncData()
    {
        Console.WriteLine("Creating PowerSyncData instance");
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Error);
        });
        var logger = loggerFactory.CreateLogger("PowerSyncLogger");

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "example.db");
        var factory = new MAUISQLiteDBOpenFactory(new MDSQLiteOpenFactoryOptions()
        {
            DbFilename = dbPath
        });
        Db = new PowerSyncDatabase(new PowerSyncDatabaseOptions()
        {
            Database = factory,
            Schema = AppSchema.PowerSyncSchema,
            Logger = logger
        });

        var nodeConnector = new NodeConnector();
        UserId = nodeConnector.UserId;

        Db.Connect(nodeConnector);
    }

    public async Task SaveListAsync(TodoList list)
    {
        if (list.ID != "")
        {
            await Db.Execute(
                "UPDATE lists SET name = ?, owner_id = ? WHERE id = ?",
                [list.Name, UserId, list.ID]);
        }
        else
        {
            await Db.Execute(
                "INSERT INTO lists (id, created_at, name, owner_id) VALUES (uuid(), datetime(), ?, ?)",
                [list.Name, UserId]);
        }
    }

    public async Task DeleteListAsync(TodoList list)
    {
        var listId = list.ID;
        // First delete all todo items in this list
        await Db.Execute("DELETE FROM todos WHERE list_id = ?", [listId]);
        await Db.Execute("DELETE FROM lists WHERE id = ?", [listId]);
    }

    public async Task SaveItemAsync(TodoItem item)
    {
        if (item.ID != "")
        {
            await Db.Execute(
                @"UPDATE todos 
                  SET description = ?, completed = ?, completed_at = ?, completed_by = ?
                  WHERE id = ?",
                [
                    item.Description,
                    item.Completed ? 1 : 0,
                    item.CompletedAt!,
                    item.Completed ? UserId : null,
                    item.ID
                ]);
        }
        else
        {
            await Db.Execute(
                @"INSERT INTO todos 
                  (id, list_id, description, created_at, created_by, completed, completed_at, completed_by)
                  VALUES (uuid(), ?, ?, datetime(), ?, ?, ?, ?)",
                [
                    item.ListId,
                    item.Description,
                    UserId,
                    item.Completed ? 1 : 0,
                    item.CompletedAt!,
                    item.Completed ? UserId : null
                ]);
        }
    }

    public async Task SaveTodoCompletedAsync(string todoId, bool completed)
    {
        if (completed)
        {
            await Db.Execute(
                @"UPDATE todos 
                  SET completed = 1, completed_at = datetime(), completed_by = ?
                  WHERE id = ?",
                [
                    UserId,
                    todoId
                ]);
        }
        else
        {
            await Db.Execute(
                @"UPDATE todos 
                  SET completed = 0, completed_at = NULL, completed_by = NULL
                  WHERE id = ?",
                [
                    todoId
                ]);
        }
    }

    public async Task DeleteItemAsync(TodoItem item)
    {
        await Db.Execute("DELETE FROM todos WHERE id = ?", [item.ID]);
    }
}
