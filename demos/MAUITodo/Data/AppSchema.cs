using PowerSync.Common.DB.Schema;

class AppSchema
{
    public static Table Todos = new Table
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
            ["completed"] = ColumnType.Integer,
        },
        Indexes =
        {
            ["list"] = ["list_id"],
        }
    };

    public static Table Lists = new Table
    {
        Name = "lists",
        Columns =
        {
            ["created_at"] = ColumnType.Text,
            ["name"] = ColumnType.Text,
            ["owner_id"] = ColumnType.Text
        }
    };

    public static Schema PowerSyncSchema = new Schema(Todos, Lists);
}
