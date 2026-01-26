namespace PowerSync.Common.IntegrationTests;

using PowerSync.Common.DB.Schema;

public class TestSchema
{
    public static Table Todos = new Table("todos", new Dictionary<string, ColumnType>
    {
        { "list_id", ColumnType.Text },
        { "created_at", ColumnType.Text },
        { "completed_at", ColumnType.Text },
        { "description", ColumnType.Text },
        { "created_by", ColumnType.Text },
        { "completed_by", ColumnType.Text },
        { "completed", ColumnType.Integer }
    }, new TableOptions
    {
        Indexes = new Dictionary<string, List<string>> { { "list", new List<string> { "list_id" } } }
    });

    public static Table Lists = new Table("lists", new Dictionary<string, ColumnType>
    {
        { "created_at", ColumnType.Text },
        { "name", ColumnType.Text },
        { "owner_id", ColumnType.Text }
    });

    public static Schema PowerSyncSchema = new SchemaBuilder(Todos, Lists).Build();
}
