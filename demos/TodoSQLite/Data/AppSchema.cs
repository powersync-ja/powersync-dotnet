using TodoSQLite.Models;

using PowerSync.Common.DB.Schema;

class AppSchema
{
    public static Table Todos = new Table(new Dictionary<string, ColumnType>
    {
        { "list_id", ColumnType.TEXT },
        { "created_at", ColumnType.TEXT },
        { "completed_at", ColumnType.TEXT },
        { "description", ColumnType.TEXT },
        { "created_by", ColumnType.TEXT },
        { "completed_by", ColumnType.TEXT },
        { "completed", ColumnType.INTEGER }
    }, new TableOptions
    {
        Indexes = new Dictionary<string, List<string>> { { "list", new List<string> { "list_id" } } }
    });

    public static Table Lists = new Table(new Dictionary<string, ColumnType>
    {
        { "created_at", ColumnType.TEXT },
        { "name", ColumnType.TEXT },
        { "owner_id", ColumnType.TEXT }
    });

    public static Schema PowerSyncSchema = new Schema(new Dictionary<string, Table>
    {
        { "todos", Todos },
        { "lists", Lists }
    });
}