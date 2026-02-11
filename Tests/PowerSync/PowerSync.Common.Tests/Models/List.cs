namespace PowerSync.Common.Tests.Models;

using PowerSync.Common.DB.Schema;
using PowerSync.Common.DB.Schema.Attributes;

[Table("lists")]
public class List
{
    [Column("id")]
    public string ListId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("owner_id")]
    public string OwnerId { get; set; }
}
