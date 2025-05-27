using System.Text.Json.Serialization;

namespace PowersyncDotnetTodoList.Models;

public class Todo
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
}
