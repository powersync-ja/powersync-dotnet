namespace MAUITodo.Models;

using PowerSync.Common.DB.Schema.Attributes;

[
    Table("todos", IgnoreEmptyUpdates = true),
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

    [Column("photo_id")]
    public string? PhotoId { get; set; }

    // Display-only properties. [Ignored] keeps them out of the PowerSync schema (they are not
    // real columns on the `todos` table), while [Column] still lets Dapper hydrate PhotoLocalUri
    // from the `photo_local_uri` alias produced by the attachments LEFT JOIN in the watch query.
    [Ignored]
    [Column("photo_local_uri")]
    public string? PhotoLocalUri { get; set; }

    [Ignored]
    public bool HasNoPhoto => PhotoId == null;

    [Ignored]
    public bool IsDownloading => PhotoId != null && PhotoLocalUri == null;

    [Ignored]
    public bool IsPhotoReady => PhotoLocalUri != null;
}
