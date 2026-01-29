namespace PowerSync.Common.DB.Schema;

public class SchemaFactory
{
    private List<Table> _tables;

    public SchemaFactory(params Table[] tables)
    {
        _tables = tables.ToList();
    }

    public Schema Create()
    {
        Dictionary<string, Table> tableMap = new();
        foreach (Table table in _tables)
        {
            tableMap[table.Name] = table;
        }
        return new Schema(tableMap);
    }
}
