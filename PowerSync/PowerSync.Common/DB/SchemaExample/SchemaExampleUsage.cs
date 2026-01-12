

using PowerSync.Common.DB.Schema;
using PowerSync.Common.DB.SchemaExample;

public class SchemaExampleUsage
{

    public void Test()
    {
        var schema = CreateExampleSchema();
    }

    public TableSchema CreateExampleSchema()
    {
        return new TableSchemaBuilder
        {
            Name = "assets",
            Columns =
        {
            // ColumnType.Text instead? 
            ["created_at"] = ColumnType.TEXT,
            ["make"] = ColumnType.TEXT,
            ["model"] = ColumnType.TEXT,
            ["quantity"] = ColumnType.TEXT,
        },
            Indexes =
        {
            ["makemodel"] = new PowerSync.Common.DB.SchemaExample.Index { "make", "model" }
        },
            LocalOnly = true
        }.Build();
    }
}