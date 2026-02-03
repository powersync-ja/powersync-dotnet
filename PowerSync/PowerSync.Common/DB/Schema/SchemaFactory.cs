namespace PowerSync.Common.DB.Schema;

using PowerSync.Common.DB.Schema.Attributes;

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

    public SchemaFactory(params Type[] types)
    {
        _tables = new();
        var indexes = new Dictionary<string, List<string>>();
        foreach (Type type in types)
        {
            var parser = new AttributeParser(type);
            _tables.Add(parser.GetTable());
        }
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
