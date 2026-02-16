namespace MAUITodo.Models;

using PowerSync.Common.DB.Schema.Attributes;

[Table("lists")]
public class TodoList
{
    [Column("id")]
    public string ID { get; set; } = "";

    [Column("created_at")]
    public string CreatedAt { get; set; } = null!;

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("owner_id")]
    public string OwnerId { get; set; } = null!;
}
