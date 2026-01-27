namespace PowerSync.Common.DB.Schema;

public class SchemaBuilder
{
    private List<Table> _tables;

    public SchemaBuilder(params Table[] tables)
    {
        _tables = tables.ToList();
    }

    public Schema Build()
    {
        Dictionary<string, Table> tableMap = new();
        foreach (Table table in _tables)
        {
            tableMap[table.Name] = table;
        }
        return new Schema(tableMap);
    }
}
