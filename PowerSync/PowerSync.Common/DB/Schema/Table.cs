namespace PowerSync.Common.DB.Schema;

using Newtonsoft.Json;

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

public class Table
{
    public const int MAX_AMOUNT_OF_COLUMNS = 1999;

    public Dictionary<string, ColumnType> Columns { get; set; } = new();

    public TableOptions Options { get; set; }

    public string Name { get; set; } = null!;

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
    string? ViewName
    {
        get { return Options.ViewName; }
        set { Options.ViewName = value; }
    }
    bool TrackMetadata
    {
        get { return Options.TrackMetadata; }
        set { Options.TrackMetadata = value; }
    }
    TrackPreviousOptions? TrackPreviousValues
    {
        get { return Options.TrackPreviousValues; }
        set { Options.TrackPreviousValues = value; }
    }
    bool IgnoreEmptyUpdates
    {
        get { return Options.IgnoreEmptyUpdates; }
        set { Options.IgnoreEmptyUpdates = value; }
    }

    public Table()
    {
        Options = new TableOptions();
    }

    // Mirrors the legacy syntax, as well as the syntax found in the other SDKs.
    public Table(string name, Dictionary<string, ColumnType> columns, TableOptions? options = null)
    {
        Name = name;
        Columns = columns;
        Options = options ?? new TableOptions();
    }

    internal CompiledTable Compile()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Table name is required.");
        }

        return new CompiledTable(Name, Columns, Options);
    }
}

