using Newtonsoft.Json;

namespace PowerSync.Common.DB.Schema;

public class RawTable(string name, PendingStatement put, PendingStatement delete)
{
    [JsonProperty("name")]
    public string Name { get; private set; } = name;

    [JsonProperty("put")]
    public PendingStatement Put { get; private set; } = put;

    [JsonProperty("delete")]
    public PendingStatement Delete { get; private set; } = delete;

    public void Validate()
    {
        // TODO
    }
}

public class PendingStatement
{
    [JsonProperty("sql")]
    public string SQL { get; set; }

    [JsonProperty("params")]
    public IReadOnlyList<PendingStatementParameter> Parameters { get; set; }
}

[JsonConverter(PendingStatementParameterJsonConverter)]
public abstract record PendingStatementParameter
{
    private PendingStatementParameter() { }

    /// <summary>
    /// Resolves to the ID of the affected row.
    /// </summary>
    public static readonly PendingStatementParameter Id = new();

    /// <summary>
    /// Resolves to the value of a column in the added row.
    ///
    /// This is only available for <see cref="RawTable.Put" />; in <see cref="RawTable.Delete" />
    /// statements, only the <see cref="Id" /> can be used as a value.
    /// </summary>
    public static PendingStatementParameter Column(string name) => new ValueColumn(name);

    /// <summary>
    /// Resolves to a JSON object containing all columns from the synced row that haven't been
    /// matched by a <see cref="Column" /> value in the same statement.
    /// </summary>
    public static readonly PendingStatementParameter Rest = new();

    /// <inheritdoc cref="Column" />
    public sealed record ValueColumn(string Name) : PendingStatementParameter;
}

internal class PendingStatementParameterJsonConverter : JsonConverter<PendingStatementParameter>
{
    // We never need to deserialize pending stamements.
    public override PendingStatementParameter ReadJson()
    {
        throw new NotImplementedException("Deserializing PendingStatementParameter is not supported.");
    }

    public override void WriteJson(JsonWriter writer, PendingStatementParameter value, JsonSerializer _serializer)
    {
        if (value == PendingStatementParameter.Id)
        {
            writer.WriteValue("Id");
        }
        else if (value is PendingStatementParameter.ValueColumn column)
        {
            JObject json = new JObject(new JProperty("Column", column.Name));
            writer.WriteValue(json);
        }
        else if (value is PendingStatementParameter.Rest)
        {
            writer.WriteValue("Rest");
        }
        else
        {
            throw new InvalidOperationException("Incorrect type for given PendingStatementParameter.");
        }
    }
}

