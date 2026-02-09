namespace MAUITodo.Models;

using PowerSync.Common.DB.Schema.Attributes;

[Table("lists")]
public class List
{
    public string ID { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Name { get; set; }

    public string OwnerID { get; set; }
}
