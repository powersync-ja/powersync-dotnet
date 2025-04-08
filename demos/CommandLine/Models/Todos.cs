namespace CommandLine.Models;

using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

[Table("todos")]
class Todo : BaseModel
{
    [PrimaryKey("id")]
    [JsonProperty("id")]
    public string Id { get; set; }

    [Column("list_id")]
    [JsonProperty("list_id")]
    public string ListId { get; set; }

    [Column("created_at")]
    [JsonProperty("created_at")]
    public string CreatedAt { get; set; }

    [Column("completed_at")]
    [JsonProperty("completed_at")]
    public string CompletedAt { get; set; }

    [Column("description")]
    [JsonProperty("description")]
    public string Description { get; set; }

    [Column("created_by")]
    [JsonProperty("created_by")]
    public string CreatedBy { get; set; }

    [Column("completed_by")]
    [JsonProperty("completed_by")]
    public string CompletedBy { get; set; }

    [Column("completed")]
    [JsonProperty("completed")]
    public int Completed { get; set; }
}
