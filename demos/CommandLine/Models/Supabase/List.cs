
using Newtonsoft.Json;

using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace CommandLine.Models.Supabase;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
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
