using Microsoft.Extensions.Logging;
using PowerSync.Common.Client;
using PowerSync.Common.MDSQLite;
using PowerSync.Maui.SQLite;
using TodoSQLite.Models;

namespace TodoSQLite.Data;

public class PowerSyncData
{
    public PowerSyncDatabase _db;
    private ILogger _logger;
    private bool _isConnected;


    public PowerSyncData()
    {
        Console.WriteLine("Creating PowerSyncData instance");
    }

    public string UserId { get; set; } = "";
    public bool IsConnected 
    { 
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler ConnectionStatusChanged;

    public async Task Init()
    {
        if (_db != null) return;

        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Trace);
        });
        _logger = loggerFactory.CreateLogger("PowerSyncLogger");

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "examplsee.db");
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

        var nodeConnector = new NodeConnector();
        UserId = nodeConnector.UserId;
        
        _db.Connect(nodeConnector);
    }

    public async Task SaveListAsync(TodoList list)
    {
        await Init();
        if (list.ID != "")
        {
            await _db.Execute(
                "UPDATE lists SET name = ?, owner_id = ? WHERE id = ?",
                [list.Name, UserId, list.ID]);
        }
        else
        {
            await _db.Execute(
                "INSERT INTO lists (id, created_at, name, owner_id, created_at) VALUES (uuid(), datetime(), ?, ?, ?)",
                [list.Name, UserId, DateTime.UtcNow.ToString("o")]);
        }
    }

    public async Task DeleteListAsync(TodoList list)
    {
        await Init();
        var listId = list.ID;
        // First delete all todo items in this list
        await _db.Execute("DELETE FROM todos WHERE list_id = ?", [listId]);
        await _db.Execute("DELETE FROM lists WHERE id = ?", [listId]);
    }
    public async Task SaveItemAsync(TodoItem item)
    {
        await Init();
        if (item.ID != "")
        {
            await _db.Execute(
                @"UPDATE todos 
                  SET description = ?, completed = ?, completed_at = ?, completed_by = ?
                  WHERE id = ?",
                [
                    item.Description,
                    item.Completed ? 1 : 0,
                    item.CompletedAt,
                    UserId,
                    item.ID
                ]);
        }
        else
        {
            await _db.Execute(
                @"INSERT INTO todos 
                  (id, list_id, description, created_at, completed, created_by, created_at)
                  VALUES (uuid(), ?, ?, ?, ?, ?, datetime())",
                [
                    item.ListId,
                    item.Description,
                    item.CreatedAt,
                    item.Completed ? 1 : 0,
                    UserId
                ]);
        }
    }

    public async Task DeleteItemAsync(TodoItem item)
    {
        await Init();
        await _db.Execute("DELETE FROM todos WHERE id = ?", [item.ID]);
    }
}