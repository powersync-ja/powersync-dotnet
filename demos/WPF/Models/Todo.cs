using System.Text.Json.Serialization;

using PowerSync.Common.DB;

namespace PowersyncDotnetTodoList.Models;

public class Todo : IFromRow<Todo>
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("list_id")]
    public string ListId { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("created_by")]
    public string CreatedBy { get; init; } = string.Empty;

    [JsonPropertyName("completed")]
    public bool Completed { get; set; } = false;

    [JsonPropertyName("completed_at")]
    public string CompletedAt { get; set; } = string.Empty;

    [JsonPropertyName("completed_by")]
    public string CompletedBy { get; set; } = string.Empty;

    public static Todo FromRow(IQueryRow row) => new()
    {
        Id = row.GetString("id"),
        ListId = row.GetString("list_id"),
        Description = row.GetString("description"),
        CreatedAt = row.GetString("created_at"),
        CreatedBy = row.GetStringOrNull("created_by") ?? string.Empty,
        Completed = row.GetBool("completed"),
        CompletedAt = row.GetStringOrNull("completed_at") ?? string.Empty,
        CompletedBy = row.GetStringOrNull("completed_by") ?? string.Empty,
    };
}
