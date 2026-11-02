namespace MAUITodo.Models;

using PowerSync.Common.DB;
using PowerSync.Common.DB.Schema.Attributes;

[Table("lists", IgnoreEmptyUpdates = true)]
public class TodoList : IFromRow<TodoList>
{
    [Column("id")]
    public string ID { get; set; } = "";

    [Column("created_at")]
    public string CreatedAt { get; set; } = null!;

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("owner_id")]
    public string OwnerId { get; set; } = null!;

    public static TodoList FromRow(IQueryRow row) => new()
    {
        ID = row.GetString("id"),
        CreatedAt = row.GetString("created_at"),
        Name = row.GetString("name"),
        OwnerId = row.GetString("owner_id"),
    };
}
