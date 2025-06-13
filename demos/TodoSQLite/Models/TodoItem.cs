using Newtonsoft.Json;

namespace TodoSQLite.Models;

public class TodoItem
{
    [JsonProperty("id")]
    public string ID { get; set; } =  "";
    
    [JsonProperty("list_id")] 
    public string ListId { get; set; } = null!;
    
    [JsonProperty("created_at")]
    public string CreatedAt { get; set; }= null!;
    
    [JsonProperty("completed_at")]
    public string? CompletedAt { get; set; }
    
    [JsonProperty("description")]
    public string Description { get; set; }= null!;
    
    [JsonProperty("created_by")]
    public string CreatedBy { get; set; }= null!;
    
    [JsonProperty("completed_by")]
    public string CompletedBy { get; set; }= null!;
    
    [JsonProperty("completed")]
    public bool Completed { get; set; } = false;
}
