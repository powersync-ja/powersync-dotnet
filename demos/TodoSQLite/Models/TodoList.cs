using Newtonsoft.Json;

namespace TodoSQLite.Models;

public class TodoList
{
    [JsonProperty("id")]
    public string ID { get; set; } = "";
    
    [JsonProperty("created_at")]
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    
    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("owner_id")]
    public string OwnerId { get; set; }
} 