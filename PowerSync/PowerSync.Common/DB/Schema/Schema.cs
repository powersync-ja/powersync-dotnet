namespace PowerSync.Common.DB.Schema;

using System.Linq;

using Newtonsoft.Json;

using PowerSync.Common.DB.Schema.Attributes;

[JsonConverter(typeof(SchemaJsonConverter))]
public class Schema
{
    private readonly List<Table> _tables;
    private readonly List<RawTable> _rawTables;

    public IReadOnlyList<Table> Tables => _tables;
    public IReadOnlyList<RawTable> RawTables => _rawTables;
    public IReadOnlyList<BaseTable> AllTables => [.. _tables, .. _rawTables];

    public Schema(params BaseTable[] tables)
    {
        _tables = [.. tables.OfType<Table>()];
        _rawTables = [.. tables.OfType<RawTable>()];
    }

    public Schema(params Type[] tables)
    {
        _tables = [];
        _rawTables = [];
        foreach (Type type in tables)
        {
            RegisterType(type);
        }
    }

    public Schema(IReadOnlyList<Type> tables, IReadOnlyList<RawTable> rawTables)
    {
        _tables = [];
        _rawTables = [.. rawTables];
        foreach (Type type in tables)
        {
            RegisterType(type);
        }
    }

    private void RegisterType(Type type)
    {
        var parser = new AttributeParser(type);
        parser.RegisterDapperTypeMap();
        _tables.Add(parser.ParseTable());
    }

    public void Validate()
    {
        foreach (var table in _tables)
        {
            table.Validate();
        }
        foreach (var rawTable in _rawTables)
        {
            rawTable.Validate();
        }
    }
}

/// <summary>
/// Serializes a <see cref="Schema" /> into the JSON format expected by the
/// `powersync_replace_schema` SQLite function.
/// </summary>
public class SchemaJsonConverter : JsonConverter<Schema>
{
    public override bool CanRead => false;

    public override Schema ReadJson(JsonReader reader, Type objectType, Schema? existingValue, bool hasExistingValue, JsonSerializer serializer)
        => throw new NotSupportedException("Deserializing Schema is not supported.");

    public override void WriteJson(JsonWriter writer, Schema? value, JsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(value);

        serializer.Serialize(writer, new { tables = value.Tables, raw_tables = value.RawTables });
    }
}
