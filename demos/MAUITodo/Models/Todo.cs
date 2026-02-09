namespace MAUITodo.Models;

using PowerSync.Common.DB.Schema;
using PowerSync.Common.DB.Schema.Attributes;

[Table("todos")]
public class Todo
{
    [Column("id")]
    public string ID { get; set; }

    [Column("list_id")]
    public string ListID { get; set; }

    [Column("created_at")]
    public string CreatedAt { get; set; }

    [Column("completed_at")]
    public string? CompletedAt { get; set; }

    [Column("description")]
    public string Description { get; set; }

    [Column("created_by")]
    public string CreatedBy { get; set; }

    [Column("completed_by")]
    public string? CompletedBy { get; set; }

    [Column("completed")]
    public bool Completed { get; set; }
}
