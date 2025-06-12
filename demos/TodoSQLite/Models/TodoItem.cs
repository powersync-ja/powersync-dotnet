using Newtonsoft.Json;

namespace TodoSQLite.Models;

public class TodoItem
{
    [JsonProperty("id")]
    public string ID { get; set; } = "";
    
    [JsonProperty("list_id")]
    public string ListId { get; set; }
    
    [JsonProperty("created_at")]
    public string CreatedAt { get; set; }
    
    [JsonProperty("completed_at")]
    public string CompletedAt { get; set; }
    
    [JsonProperty("description")]
    public string Description { get; set; }
    
    [JsonProperty("created_by")]
    public string CreatedBy { get; set; }
    
    [JsonProperty("completed_by")]
    public string CompletedBy { get; set; }
    
    [JsonProperty("completed")]
    public bool Completed { get; set; }
}
