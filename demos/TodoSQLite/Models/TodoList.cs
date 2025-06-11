using SQLite;

namespace TodoSQLite.Models;

public class TodoList
{
    [PrimaryKey, AutoIncrement]
    public int ID { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string Name { get; set; }
    public string OwnerId { get; set; }
} 