namespace PowerSync.Common.Tests.Models;

using PowerSync.Common.DB.Schema;
using PowerSync.Common.DB.Schema.Attributes;

[
    Table("todos"),
    Index("list", ["list_id"])
]
public class Todo
{
    [Column("id")]
    public string TodoId { get; set; }

    [Column("list_id")]
    public string ListId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("description")]
    public string Description { get; set; }

    [Column("created_by")]
    public string CreatedBy { get; set; }

    [Column("completed_by")]
    public string? CompletedBy { get; set; }

    [Column("completed")]
    public bool Completed { get; set; }

}
