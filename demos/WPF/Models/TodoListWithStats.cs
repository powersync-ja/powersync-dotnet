using System.Text.Json.Serialization;

namespace PowersyncDotnetTodoList.Models;

public class TodoListWithStats : TodoList
{
    [JsonPropertyName("pending_tasks")]
    public int PendingTasks { get; set; } = 0;

    [JsonPropertyName("completed_tasks")]
    public int CompletedTasks { get; set; } = 0;
}
