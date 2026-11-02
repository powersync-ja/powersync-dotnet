namespace PowerSync.Common.Tests.Models;

using PowerSync.Common.DB;
using PowerSync.Common.DB.Schema;
using PowerSync.Common.DB.Schema.Attributes;

[
    Table("todos"),
    Index("list", ["list_id"])
]
public class Todo : IFromRow<Todo>
{
    [Column("id")]
    public string TodoId { get; set; } = "";

    [Column("list_id")]
    public string ListId { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("description")]
    public string Description { get; set; } = "";

    [Column("created_by")]
    public string CreatedBy { get; set; } = "";

    [Column("completed_by")]
    public string? CompletedBy { get; set; }

    [Column("completed")]
    public bool Completed { get; set; }

    public static Todo FromRow(IQueryRow row) => new()
    {
        TodoId = row.GetString("id"),
        ListId = row.GetStringOrNull("list_id") ?? "",
        CreatedAt = row.GetDateTime("created_at"),
        CompletedAt = row.GetDateTimeOrNull("completed_at"),
        Description = row.GetString("description"),
        CreatedBy = row.GetStringOrNull("created_by") ?? "",
        CompletedBy = row.GetStringOrNull("completed_by"),
        Completed = row.GetBool("completed"),
    };
}
