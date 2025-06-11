using SQLite;

namespace TodoSQLite.Models;

public class TodoItem
{
    [PrimaryKey]
    public string ID { get; set; } = "";
    public string ListId { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string CompletedAt { get; set; }
    public string Description { get; set; }
    public string CreatedBy { get; set; }
    public string CompletedBy { get; set; }
    public bool Completed { get; set; }
}
