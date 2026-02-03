namespace CommandLine.Schema;

using PowerSync.Common.DB.Schema;
using PowerSync.Common.DB.Schema.Attributes;

[
    Table("todos"),
    Index("list", ["list_id"]),
    Index("created_at", ["created_at"])
]
class Todo
{
    public string id { get; set; }
    public string list_id { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? completed_at { get; set; }
    public string created_by { get; set; }
    public string? completed_by { get; set; }
    public bool completed { get; set; }
}

[Table("lists")]
class List
{
    public string id { get; set; }
    public string created_at { get; set; }
    public string name { get; set; }
    public string owner_id { get; set; }
}

class AppSchema
{
    public static Schema PowerSyncSchema = new SchemaFactory(typeof(Todo), typeof(List)).Create();
}
