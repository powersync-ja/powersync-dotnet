namespace PowerSync.Common.DB.Schema;

public class SchemaFactory
{
    private readonly List<Table> _tables;

    public SchemaFactory(params Table[] tables)
    {
        _tables = tables.ToList();
    }

    public SchemaFactory(params TableFactory[] tableFactories)
    {
        _tables = tableFactories.Select((f) => f.Create()).ToList();
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
