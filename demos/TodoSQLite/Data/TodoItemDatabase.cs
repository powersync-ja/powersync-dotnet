using SQLite;
using TodoSQLite.Models;

namespace TodoSQLite.Data;

public class TodoItemDatabase
{
    SQLiteAsyncConnection database;

    async Task Init()
    {
        if (database is not null)
            return;

        database = new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags);
        await database.CreateTableAsync<TodoList>();
        await database.CreateTableAsync<TodoItem>();
    }

    // List operations
    public async Task<List<TodoList>> GetListsAsync()
    {
        await Init();
        return await database.Table<TodoList>().OrderByDescending(l => l.CreatedAt).ToListAsync();
    }

    public async Task<TodoList> GetListAsync(int id)
    {
        await Init();
        return await database.Table<TodoList>().Where(l => l.ID == id).FirstOrDefaultAsync();
    }

    public async Task<int> SaveListAsync(TodoList list)
    {
        await Init();
        if (list.ID != 0)
        {
            return await database.UpdateAsync(list);
        }
        else
        {
            return await database.InsertAsync(list);
        }
    }

    public async Task<int> DeleteListAsync(TodoList list)
    {
        await Init();
        // First delete all todo items in this list
        await database.Table<TodoItem>().Where(t => t.ListId == list.ID).DeleteAsync();
        return await database.DeleteAsync(list);
    }

    // Todo item operations
    public async Task<List<TodoItem>> GetItemsAsync(int listId)
    {
        await Init();
        return await database.Table<TodoItem>()
            .Where(t => t.ListId == listId)
            .OrderByDescending(t => t.ID)
            .ToListAsync();
    }

    public async Task<List<TodoItem>> GetItemsNotDoneAsync(int listId)
    {
        await Init();
        return await database.Table<TodoItem>()
            .Where(t => t.ListId == listId && !t.Done)
            .OrderByDescending(t => t.ID)
            .ToListAsync();
    }

    public async Task<TodoItem> GetItemAsync(int id)
    {
        await Init();
        return await database.Table<TodoItem>().Where(i => i.ID == id).FirstOrDefaultAsync();
    }

    public async Task<int> SaveItemAsync(TodoItem item)
    {
        await Init();
        if (item.ID != 0)
        {
            return await database.UpdateAsync(item);
        }
        else
        {
            return await database.InsertAsync(item);
        }
    }

    public async Task<int> DeleteItemAsync(TodoItem item)
    {
        await Init();
        return await database.DeleteAsync(item);
    }
}