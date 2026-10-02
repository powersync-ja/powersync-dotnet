using System.Text.RegularExpressions;

using Newtonsoft.Json;

using PowerSync.Common.DB.Schema.Attributes;
using PowerSync.Common.Utils.Converters;

namespace PowerSync.Common.DB.Schema;

public class TableOptions(
    Dictionary<string, List<string>>? indexes = null,
    bool? localOnly = null,
    bool? insertOnly = null,
    string? viewName = null,
    bool? trackMetadata = null,
    TrackPreviousOptions? trackPreviousValues = null,
    bool? ignoreEmptyUpdates = null
)
{
    public Dictionary<string, List<string>> Indexes { get; set; } = indexes ?? [];

    public bool LocalOnly { get; set; } = localOnly ?? false;

    public bool InsertOnly { get; set; } = insertOnly ?? false;

    public string? ViewName { get; set; } = viewName;

    /// <summary>
    /// Whether to add a hidden `_metadata` column that will be enabled for updates to attach custom
    /// information about writes that will be reported through [CrudEntry.metadata].
    /// </summary>
    public bool TrackMetadata { get; set; } = trackMetadata ?? false;

    /// <summary>
    /// When set to a non-null value, track old values of columns
    /// </summary>
    public TrackPreviousOptions? TrackPreviousValues { get; set; } = trackPreviousValues;

    /// <summary>
    /// Whether an `UPDATE` statement that doesn't change any values should be ignored when creating
    /// CRUD entries.
    /// </summary>
    public bool IgnoreEmptyUpdates { get; set; } = ignoreEmptyUpdates ?? false;

    public void Validate()
    {
        if (TrackMetadata && LocalOnly)
        {
            throw new Exception("Can't include metadata for local-only tables.");
        }

        if (TrackPreviousValues != null && LocalOnly)
        {
            throw new Exception("Can't include old values for local-only tables.");
        }
    }

    /// <summary>
    /// Serializes properties to a JsonWriter without wrapping in an object.
    ///
    /// Does not write the view name or indexes, since this method is used
    /// with both regular tables and raw tables.
    /// </summary>
    internal void WriteJsonSharedProperties(JsonWriter writer, JsonSerializer serializer)
    {
        writer.WritePropertyName("local_only");
        writer.WriteValue(LocalOnly);

        writer.WritePropertyName("insert_only");
        writer.WriteValue(InsertOnly);

        writer.WritePropertyName("ignore_empty_update");
        writer.WriteValue(IgnoreEmptyUpdates);

        writer.WritePropertyName("include_metadata");
        writer.WriteValue(TrackMetadata);

        if (TrackPreviousValues is { } trackPrevious)
        {
            writer.WritePropertyName("include_old");
            if (trackPrevious.Columns is null)
            {
                writer.WriteValue(true);
            }
            else
            {
                serializer.Serialize(writer, trackPrevious.Columns);
            }

            writer.WritePropertyName("include_old_only_when_changed");
            writer.WriteValue(trackPrevious.OnlyWhenChanged ?? false);
        }
    }
}

/// <summary>
/// Whether to include previous column values when PowerSync tracks local changes.
/// Including old values may be helpful for some backend connector implementations,
/// which is why it can be enabled on a per-table or per-column basis.
/// </summary>
public class TrackPreviousOptions
{
    /// <summary>
    /// When defined, a list of column names for which old values should be tracked.
    /// </summary>
    [JsonProperty("columns")]
    public List<string>? Columns { get; set; }

    /// <summary>
    /// When enabled, only include values that have actually been changed by an update.
    /// </summary>
    [JsonProperty("onlyWhenChanged")]
    public bool? OnlyWhenChanged { get; set; }
}

[JsonConverter(typeof(TableJsonConverter))]
public class Table : BaseTable
{
    public static readonly Regex InvalidSQLCharacters = new Regex(@"[""'%,.#\s\[\]]", RegexOptions.Compiled);

    public const int MAX_AMOUNT_OF_COLUMNS = 1999;

    public override string Name { get; set; }

    public Dictionary<string, ColumnType> Columns { get; set; }
    public TableOptions Options { get; set; }

    // Accessors
    public Dictionary<string, List<string>> Indexes
    {
        get { return Options.Indexes; }
        set { Options.Indexes = value; }
    }
    public bool LocalOnly
    {
        get { return Options.LocalOnly; }
        set { Options.LocalOnly = value; }
    }
    public bool InsertOnly
    {
        get { return Options.InsertOnly; }
        set { Options.InsertOnly = value; }
    }
    public string? ViewName
    {
        get { return Options.ViewName; }
        set { Options.ViewName = value; }
    }
    public bool TrackMetadata
    {
        get { return Options.TrackMetadata; }
        set { Options.TrackMetadata = value; }
    }
    public TrackPreviousOptions? TrackPreviousValues
    {
        get { return Options.TrackPreviousValues; }
        set { Options.TrackPreviousValues = value; }
    }
    public bool IgnoreEmptyUpdates
    {
        get { return Options.IgnoreEmptyUpdates; }
        set { Options.IgnoreEmptyUpdates = value; }
    }

    public Table()
    {
        Name = "";
        Columns = [];
        Options = new TableOptions();
    }

    /// <summary>
    /// Generate a table implementation from a Type object and registers its shape with the
    /// internal Dapper type mapper.
    ///
    /// The given type is required to have the <see cref="TableAttribute" /> attribute.
    /// </summary>
    public Table(Type type, TableOptions? options = null)
    {
        var parser = new AttributeParser(type);
        Name = parser.TableName;
        Columns = parser.ParseColumns();
        Options = options ?? parser.ParseTableOptions();
        parser.RegisterDapperTypeMap();
    }

    /// <summary>
    /// Clone the table "<paramref name="other" />" with an optional override for table options.
    /// </summary>
    public Table(Table other, TableOptions? options = null)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));

        Name = other.Name;
        Columns = other.Columns;
        Options = options ?? other.Options;
    }

    public Table(string name, Dictionary<string, ColumnType> columns, TableOptions? options = null)
    {
        Name = name;
        Columns = columns;
        Options = options ?? new TableOptions();
    }

    public override void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new Exception($"Table name is required.");
        }

        if (InvalidSQLCharacters.IsMatch(Name))
        {
            throw new Exception($"Invalid characters in table name: {Name}");
        }

        if (!string.IsNullOrWhiteSpace(Options.ViewName) && InvalidSQLCharacters.IsMatch(Options.ViewName))
        {
            throw new Exception($"Invalid characters in view name: {Options.ViewName}");
        }

        if (Columns.Count > MAX_AMOUNT_OF_COLUMNS)
        {
            throw new Exception(
                $"Table has too many columns. The maximum number of columns is {MAX_AMOUNT_OF_COLUMNS}.");
        }

        Options.Validate();

        var columnNames = new HashSet<string> { "id" };

        foreach (var kvp in Columns)
        {
            string columnName = kvp.Key;
            ColumnType columnType = kvp.Value;

            if (columnName == "id")
            {
                throw new Exception("An id column is automatically added, custom id columns are not supported");
            }

            if (columnType == ColumnType.Inferred)
            {
                throw new Exception($"Invalid ColumnType for {kvp.Key}: ColumnType.Inferred. ColumnType.Inferred is only supported when using the schema attribute syntax for defining tables.");
            }

            if (InvalidSQLCharacters.IsMatch(columnName))
            {
                throw new Exception($"Invalid characters in column name: {columnName}");
            }

            columnNames.Add(columnName);
        }

        foreach (var index in Indexes)
        {
            var indexName = index.Key;
            var indexColumns = index.Value;

            if (InvalidSQLCharacters.IsMatch(indexName))
            {
                throw new Exception($"Invalid characters in index name: {indexName}");
            }

            foreach (var column in indexColumns)
            {
                // A leading "-" denotes a descending index on the column.
                var columnName = column.StartsWith("-") ? column.Substring(1) : column;

                if (!columnNames.Contains(columnName))
                {
                    throw new Exception($"Column {column} not found for index {indexName}");
                }
            }
        }
    }
}

/// <summary>
/// Serializes a <see cref="Table" /> into the JSON format expected by the
/// `powersync_replace_schema` SQLite function.
/// </summary>
internal class TableJsonConverter : JsonConverter<Table>
{
    public override bool CanRead => false;

    public override Table ReadJson(JsonReader reader, Type objectType, Table? existingValue, bool hasExistingValue, JsonSerializer serializer)
        => throw new NotSupportedException("Deserializing Table is not supported.");

    public override void WriteJson(JsonWriter writer, Table? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartObject();

        writer.WritePropertyName("name");
        writer.WriteValue(value.Name);

        writer.WritePropertyName("columns");
        WriteColumns(writer, value!);

        writer.WritePropertyName("view_name");
        writer.WriteValue(value.Options.ViewName ?? value.Name);

        writer.WritePropertyName("indexes");
        WriteIndexes(writer, value!);

        value.Options.WriteJsonSharedProperties(writer, serializer);

        writer.WriteEndObject();
    }

    private static void WriteColumns(JsonWriter writer, Table value)
    {
        writer.WriteStartArray();
        foreach (var (name, type) in value.Columns)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("name");
            writer.WriteValue(name);

            writer.WritePropertyName("type");
            writer.WriteValue(type.ToString());

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    internal void WriteIndexes(JsonWriter writer, Table value)
    {
        writer.WriteStartArray();
        foreach (var (name, columns) in value.Options.Indexes)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("name");
            writer.WriteValue(name);

            writer.WritePropertyName("columns");
            writer.WriteStartArray();
            foreach (var column in columns)
            {
                string columnName;
                bool asc;
                string columnType;

                try
                {
                    // Strip leading '-' from descending columns
                    asc = column[0] != '-';
                    columnName = asc
                        ? column
                        : column[1..];
                    columnType = value.Columns[columnName].ToString();
                }
                catch (Exception e)
                {
                    throw new Exception($"Failed to parse indexes for table \"{value.Name}\" during serialization. Check that all your tables' indexes are correct.", e);
                }

                writer.WriteStartObject();

                writer.WritePropertyName("name");
                writer.WriteValue(columnName);

                writer.WritePropertyName("ascending");
                writer.WriteValue(asc);

                writer.WritePropertyName("type");
                writer.WriteValue(columnType);

                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
