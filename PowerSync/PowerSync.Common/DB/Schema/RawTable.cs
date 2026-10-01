using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PowerSync.Common.DB.Schema;

[JsonConverter(typeof(RawTableJsonConverter))]
public class RawTable : BaseTable
{
    public override string Name { get; set; }
    public RawTableSchema? Schema { get; set; }
    public PendingStatement? Put { get; set; }
    public PendingStatement? Delete { get; set; }
    public string? Clear { get; set; }

    public RawTable(string name, RawTableSchema schema, string? clear = null)
    {
        Name = name;
        Schema = schema;
        Clear = clear;
    }

    public RawTable(string name, PendingStatement put, PendingStatement delete, string? clear = null)
    {
        Name = name;
        Put = put;
        Delete = delete;
        Clear = clear;
    }

    public override void Validate()
    {
        if (Schema is not null)
        {
            Schema.Validate();
        }
        else
        {
            if (Put is null || Delete is null)
            {
                throw new InvalidOperationException("Raw tables without a schema need to provide put and delete statements.");
            }
        }
    }
}

/// <summary>
/// The schema of a [RawTable] in the local database.
///
/// This information is optional when declaring raw tables. However, providing it allows the sync
/// client to infer [RawTable.put] and [RawTable.delete] statements automatically.
/// </summary>
public class RawTableSchema
{
    /// <summary>
    /// The actual name of the raw table in the local schema.
    ///
    /// While <see cref="RawTable.Name" /> specifies the name of the synced tables to match,
    /// this specifies the name of the table in the local SQLite database. This is
    /// used to infer statements for the sync client. It can also be used to auto-generate
    /// triggers forwarding writes on raw tables into the CRUD upload queue.
    ///
    /// When set to null, defaults to <see cref="RawTable.Name" />.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// An optional filter of columns that should be synced.
    ///
    /// By default, all columns in a raw table are considered to be synced. If a filter is specified,
    /// PowerSync treats unmatched columns as local-only and will not attempt to sync them.
    /// </summary>
    public List<string>? SyncedColumns { get; set; }

    /// <summary>
    /// Common options affecting how the `powersync_create_raw_table_crud_trigger` SQL function
    /// generates triggers.
    /// </summary>
    public TableOptions Options { get; set; } = new();

    public void Validate()
    {
        Options.Validate();
    }
}

public class PendingStatement(string sql, IReadOnlyList<PendingStatementParameter> parameters)
{
    [JsonProperty("sql")]
    public string SQL { get; set; } = sql;

    [JsonProperty("params")]
    public IReadOnlyList<PendingStatementParameter> Parameters { get; set; } = parameters;
}

[JsonConverter(typeof(PendingStatementParameterJsonConverter))]
public record PendingStatementParameter
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

internal class RawTableJsonConverter : JsonConverter<RawTable>
{
    public override RawTable ReadJson(JsonReader reader, Type objectType, RawTable? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        throw new NotSupportedException("Deserializing RawTable is not supported.");
    }

    // Flattens RawTable into { name, put, delete, clear, table_name, synced_columns, options }.
    public override void WriteJson(JsonWriter writer, RawTable? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartObject();

        // TODO: Should we include null properties or strip them?
        // TODO: Clean this up with a better implementation probably
        writer.WritePropertyName("name");
        writer.WriteValue(value.Name);

        writer.WritePropertyName("put");
        serializer.Serialize(writer, value.Put);

        writer.WritePropertyName("delete");
        serializer.Serialize(writer, value.Delete);

        writer.WritePropertyName("clear");
        writer.WriteValue(value.Name);

        writer.WritePropertyName("table_name");
        writer.WriteValue(value.Schema?.TableName ?? value.Name);

        writer.WritePropertyName("synced_columns");
        serializer.Serialize(writer, value.Schema?.SyncedColumns);

        writer.WritePropertyName("options");
        serializer.Serialize(writer, value.Schema?.Options);

        writer.WriteEndObject();
    }
}

internal class PendingStatementParameterJsonConverter : JsonConverter<PendingStatementParameter>
{
    public override PendingStatementParameter ReadJson(JsonReader reader, Type objectType, PendingStatementParameter? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        throw new NotSupportedException("Deserializing PendingStatementParameter is not supported.");
    }

    public override void WriteJson(JsonWriter writer, PendingStatementParameter? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        if (value == PendingStatementParameter.Id)
        {
            writer.WriteValue("Id");
        }
        else if (value is PendingStatementParameter.ValueColumn column)
        {
            var json = new JObject(new JProperty("Column", column.Name));
            json.WriteTo(writer);
        }
        else if (value == PendingStatementParameter.Rest)
        {
            writer.WriteValue("Rest");
        }
        else
        {
            throw new InvalidOperationException("Incorrect type for given PendingStatementParameter.");
        }
    }
}

