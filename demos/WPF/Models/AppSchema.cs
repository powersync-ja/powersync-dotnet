using PowerSync.Common.DB.Schema;

namespace PowersyncDotnetTodoList.Models;

class AppSchema
{
    public static Table Todos = new Table(
        new Dictionary<string, ColumnType>
        {
            { "list_id", ColumnType.Text },
            { "created_at", ColumnType.Text },
            { "completed_at", ColumnType.Text },
            { "description", ColumnType.Text },
            { "created_by", ColumnType.Text },
            { "completed_by", ColumnType.Text },
            { "completed", ColumnType.Integer },
        },
        new TableOptions
        {
            Indexes = new Dictionary<string, List<string>>
            {
                {
                    "list",
                    new List<string> { "list_id" }
                },
            },
        }
    );

    public static Table Lists = new Table(
        new Dictionary<string, ColumnType>
        {
            { "created_at", ColumnType.Text },
            { "name", ColumnType.Text },
            { "owner_id", ColumnType.Text },
        }
    );

    public static Schema PowerSyncSchema = new Schema(
        new Dictionary<string, Table> { { "todos", Todos }, { "lists", Lists } }
    );
}
