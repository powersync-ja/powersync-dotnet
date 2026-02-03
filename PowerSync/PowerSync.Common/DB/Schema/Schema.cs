namespace PowerSync.Common.DB.Schema;

public class Schema
{
    private List<Table> _tables;

    public Schema(params Table[] tables)
    {
        _tables = tables.ToList();
    }

    internal CompiledSchema Compile()
    {
        Dictionary<string, CompiledTable> tableMap = new();
        foreach (Table table in _tables)
        {
            var compiled = table.Compile();
            tableMap[compiled.Name] = compiled;
        }
        return new CompiledSchema(tableMap);
    }
}
