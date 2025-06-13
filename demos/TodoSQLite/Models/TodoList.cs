using Newtonsoft.Json;

namespace TodoSQLite.Models;

public class TodoList
{
    [JsonProperty("id")]
    public string ID { get; set; }  = "";
    
    [JsonProperty("created_at")]
    public string CreatedAt { get; set; } = null!;
    
    [JsonProperty("name")]
    public string Name { get; set; } = null!;
    
    [JsonProperty("owner_id")]
    public string OwnerId { get; set; }= null!;
} 