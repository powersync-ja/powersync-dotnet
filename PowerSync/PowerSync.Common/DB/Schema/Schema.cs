namespace PowerSync.Common.DB.Schema;

using PowerSync.Common.DB.Schema.Attributes;

public class Schema
{
    private readonly List<Table> _tables;

    public Schema(params Table[] tables)
    {
        _tables = tables.ToList();
    }

    public Schema(params Type[] types)
    {
        _tables = new();
        var indexes = new Dictionary<string, List<string>>();
        foreach (Type type in types)
        {
            var parser = new AttributeParser(type);
            _tables.Add(parser.ParseTable());
        }
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
