namespace MAUITodo.Models;

using PowerSync.Common.DB.Schema.Attributes;

[
    Table("todos"),
    Index("list", ["list_id"])
]
public class TodoItem
{
    [Column("id")]
    public string ID { get; set; } = "";

    [Column("list_id")]
    public string ListId { get; set; } = null!;

    [Column("created_at")]
    public string CreatedAt { get; set; } = null!;

    [Column("completed_at")]
    public string? CompletedAt { get; set; }

    [Column("description")]
    public string Description { get; set; } = null!;

    [Column("created_by")]
    public string CreatedBy { get; set; } = null!;

    [Column("completed_by")]
    public string CompletedBy { get; set; } = null!;

    [Column("completed")]
    public bool Completed { get; set; }
}
