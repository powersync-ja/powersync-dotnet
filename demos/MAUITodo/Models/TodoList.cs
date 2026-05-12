namespace MAUITodo.Models;

using Newtonsoft.Json;
using Supabase.Postgrest.Models;

using PowerSync = PowerSync.Common.DB.Schema.Attributes;
using Supabase = Supabase.Postgrest.Attributes;

// TODO: We should probably be able to automatically infer the PowerSync
// model from the Supabase model or vice-versa.
[
    PowerSync.Table("lists"),
    Supabase.Table("lists")
]
public class TodoList : BaseModel
{
    [PowerSync.Column("id")]
    [Supabase.Column("id")]
    [JsonProperty("id")]
    [Supabase.PrimaryKey("id")]
    public string ID { get; set; } = "";

    [PowerSync.Column("created_at")]
    [Supabase.Column("created_at")]
    [JsonProperty("created_at")]
    public string CreatedAt { get; set; } = null!;

    [PowerSync.Column("name")]
    [Supabase.Column("name")]
    [JsonProperty("name")]
    public string Name { get; set; } = null!;

    [PowerSync.Column("owner_id")]
    [Supabase.Column("owner_id")]
    [JsonProperty("owner_id")]
    public string OwnerId { get; set; } = null!;
}
