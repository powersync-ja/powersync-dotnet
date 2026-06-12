namespace PowerSync.Common.DB.Schema;

using System.Text.RegularExpressions;

using Newtonsoft.Json;

using PowerSync.Common.DB.Schema.Attributes;

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
public class Table
{
    public static readonly Regex InvalidSQLCharacters = new Regex(@"[""'%,.#\s\[\]]", RegexOptions.Compiled);

    public const int MAX_AMOUNT_OF_COLUMNS = 1999;

    public string Name { get; set; }

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

    public void Validate()
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

        if (Options.TrackMetadata && Options.LocalOnly)
        {
            throw new Exception("Can't include metadata for local-only tables.");
        }

        if (Options.TrackPreviousValues != null && Options.LocalOnly)
        {
            throw new Exception("Can't include old values for local-only tables.");
        }

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
public class TableJsonConverter : JsonConverter<Table>
{
    public override bool CanRead => false;

    public override Table ReadJson(JsonReader reader, Type objectType, Table? existingValue, bool hasExistingValue, JsonSerializer serializer)
        => throw new NotSupportedException("Deserializing a Table from JSON is not supported.");

    public override void WriteJson(JsonWriter writer, Table? value, JsonSerializer serializer)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));

        var trackPrevious = value.TrackPreviousValues;

        serializer.Serialize(writer, new
        {
            name = value.Name,
            view_name = value.ViewName ?? value.Name,
            local_only = value.LocalOnly,
            insert_only = value.InsertOnly,
            columns = value.Columns.Select(column => column.Value == ColumnType.Inferred
                ? throw new InvalidOperationException($"Attempted to serialise Inferred column {column.Key}. ColumnType.Inferred is only valid as an argument to ColumnAttribute.")
                : new { name = column.Key, type = column.Value.ToString() }),
            indexes = value.Indexes.Select(index => new
            {
                name = index.Key,
                columns = index.Value.Select(column =>
                {
                    // A leading "-" denotes a descending index on the column.
                    var descending = column.StartsWith("-");
                    var columnName = descending ? column.Substring(1) : column;
                    return new
                    {
                        name = columnName,
                        ascending = !descending,
                        type = (value.Columns.TryGetValue(columnName, out var columnType) ? columnType : default).ToString()
                    };
                })
            }),
            include_metadata = value.TrackMetadata,
            ignore_empty_update = value.IgnoreEmptyUpdates,
            // false when disabled, true when tracking all columns, or a list of tracked column names.
            include_old = (object)(trackPrevious switch
            {
                null => false,
                { Columns: null } => true,
                { Columns: var columns } => columns
            }),
            include_old_only_when_changed = trackPrevious?.OnlyWhenChanged ?? false
        });
    }
}

