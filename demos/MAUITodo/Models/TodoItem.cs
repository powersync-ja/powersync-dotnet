namespace MAUITodo.Models;

using Newtonsoft.Json;
using Supabase.Postgrest.Models;

using PowerSync = PowerSync.Common.DB.Schema.Attributes;
using Supabase = Supabase.Postgrest.Attributes;

// TODO: We should probably be able to automatically infer the PowerSync
// model from the Supabase model or vice-versa.
[
    PowerSync.Table("todos"),
    PowerSync.Index("list", ["list_id"]),
    Supabase.Table("todos")
]
public class TodoItem : BaseModel
{
    [PowerSync.Column("id")]
    [Supabase.Column("id")]
    [Supabase.PrimaryKey("id")]
    [JsonProperty("id")]
    public string ID { get; set; } = "";

    [PowerSync.Column("list_id")]
    [Supabase.Column("list_id")]
    [JsonProperty("list_id")]
    public string ListId { get; set; } = null!;

    [PowerSync.Column("created_at")]
    [Supabase.Column("created_at")]
    [JsonProperty("created_at")]
    public string CreatedAt { get; set; } = null!;

    [PowerSync.Column("completed_at")]
    [Supabase.Column("completed_at")]
    [JsonProperty("completed_at")]
    public string? CompletedAt { get; set; }

    [PowerSync.Column("description")]
    [Supabase.Column("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = null!;

    [PowerSync.Column("created_by")]
    [Supabase.Column("created_by")]
    [JsonProperty("created_by")]
    public string CreatedBy { get; set; } = null!;

    [PowerSync.Column("completed_by")]
    [Supabase.Column("completed_by")]
    [JsonProperty("completed_by")]
    public string CompletedBy { get; set; } = null!;

    [PowerSync.Column("completed")]
    [Supabase.Column("completed")]
    [JsonProperty("completed")]
    public bool Completed { get; set; }
}
