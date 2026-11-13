using System.Text.Json.Serialization;

using PowerSync.Common.DB;

namespace PowersyncDotnetTodoList.Models;

public class TodoListWithStats : TodoList, IFromRow<TodoListWithStats>
{
    [JsonPropertyName("pending_tasks")]
    public int PendingTasks { get; set; } = 0;

    [JsonPropertyName("completed_tasks")]
    public int CompletedTasks { get; set; } = 0;

    public static TodoListWithStats FromRow(IQueryRow row) => new()
    {
        Id = row.GetString("id"),
        Name = row.GetString("name"),
        OwnerId = row.GetString("owner_id"),
        CreatedAt = row.GetString("created_at"),
        PendingTasks = row.GetInt("PendingTasks"),
        CompletedTasks = row.GetInt("CompletedTasks"),
    };
}
