namespace PowerSync.Common.DB.Schema;

using Newtonsoft.Json;

using PowerSync.Common.DB.Schema.Attributes;

[JsonConverter(typeof(SchemaJsonConverter))]
public class Schema
{
    private readonly List<Table> _tables;
    private readonly List<RawTable> _rawTables;

    public IReadOnlyList<Table> Tables => _tables;
    public IReadOnlyList<RawTable> RawTables => _rawTables;

    public Schema(params Table[] tables)
    {
        _tables = [.. tables];
        _rawTables = [];
    }

    public Schema(params RawTable[] rawTables)
    {
        _tables = [];
        _rawTables = [.. rawTables];
    }

    public Schema(List<Table> tables, List<RawTable> rawTables)
    {
        _tables = tables;
        _rawTables = rawTables;
    }

    public Schema(params Type[] types)
    {
        _tables = [];
        _rawTables = [];
        // TODO: Should there be a mechanism for creating raw tables from types?
        foreach (Type type in types)
        {
            var parser = new AttributeParser(type);
            parser.RegisterDapperTypeMap();
            _tables.Add(parser.ParseTable());
        }
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
        => throw new NotSupportedException("Deserializing a Schema from JSON is not supported.");

    public override void WriteJson(JsonWriter writer, Schema? value, JsonSerializer serializer)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));

        serializer.Serialize(writer, new { tables = value.Tables });
    }
}
