using PowerSync.Common.DB.Schema;

class AppSchema
{
    public static Table Todos = new TableBuilder()
    {
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
    }.Build();

    public static Table Lists = new TableBuilder()
    {
        Columns =
        {
            ["created_at"] = ColumnType.Text,
            ["name"] = ColumnType.Text,
            ["owner_id"] = ColumnType.Text
        }
    }.Build();

    public static Schema PowerSyncSchema = new Schema(new Dictionary<string, Table>
    {
        { "todos", Todos },
        { "lists", Lists }
    });
}
