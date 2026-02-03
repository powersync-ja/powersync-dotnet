namespace PowersyncDotnetTodoList.Models;

using PowerSync.Common.DB.Schema;

class AppSchema
{
    public static Table Todos = new TableFactory()
    {
        Name = "todos",
        Columns =
        {
            ["list_id"] = ColumnType.Text,
            ["created_at"] = ColumnType.Text,
            ["completed_at"] = ColumnType.Text,
            ["description"] = ColumnType.Text,
            ["created_by"] = ColumnType.Text,
            ["completed_by"] = ColumnType.Text,
            ["completed"] = ColumnType.Integer
        },
        Indexes =
        {
            ["list"] = ["list_id"],
            ["created_at"] = ["created_at"],
        }
    };

    public static Table Lists = new TableFactory()
    {
        Name = "lists",
        Columns = {
            ["created_at"] = ColumnType.Text,
            ["name"] = ColumnType.Text,
            ["owner_id"] = ColumnType.Text
        }
    };

    public static CompiledSchema PowerSyncSchema = new SchemaFactory(Todos, Lists).Create();
}
