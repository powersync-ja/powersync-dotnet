namespace CommandLine.Models;

using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

[Table("lists")]
class List : BaseModel
{
    [PrimaryKey("id")]
    [JsonProperty("id")]
    public string Id { get; set; }

    [Column("created_at")]
    [JsonProperty("created_at")]
    public string CreatedAt { get; set; }

    [Column("name")]
    [JsonProperty("name")]
    public string Name { get; set; }

    [Column("owner_id")]
    [JsonProperty("owner_id")]
    public string OwnerId { get; set; }
}
