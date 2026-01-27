namespace PowerSync.Common.DB.Schema;

using System.Text.RegularExpressions;

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

    public string? ViewName { get; } = viewName;

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

    public static readonly Regex InvalidSQLCharacters = new Regex(@"[""'%,.#\s\[\]]", RegexOptions.Compiled);

    public string Name { get; init; } = null!;
    protected TableOptions Options { get; init; } = null!;

    public Dictionary<string, ColumnType> Columns;
    public Dictionary<string, List<string>> Indexes;

    // TODO recalculate when Columns or Indexes changes, or make Columns and Indexes readonly?
    private readonly List<ColumnJSON> ColumnsJSON;
    private readonly List<IndexJSON> IndexesJSON;

    public Table(string name, Dictionary<string, ColumnType> columns, TableOptions? options = null)
    {
        ColumnsJSON =
            columns
            .Select(kvp => new ColumnJSON(new ColumnJSONOptions(kvp.Key, kvp.Value)))
            .ToList();

        IndexesJSON =
            (Options?.Indexes ?? [])
            .Select(kvp =>
                new IndexJSON(new IndexJSONOptions(
                    kvp.Key,
                    kvp.Value.Select(name =>
                        new IndexedColumnJSON(new IndexedColumnJSONOptions(
                            name.Replace("-", ""), !name.StartsWith("-")))
                    ).ToList()
                ))
            )
            .ToList();

        Options = options ?? new TableOptions();

        Name = name;
        Columns = columns;
        Indexes = Options?.Indexes ?? [];
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new Exception($"Table name is required.");
        }

        if (!string.IsNullOrWhiteSpace(Options.ViewName) && InvalidSQLCharacters.IsMatch(Options.ViewName!))
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

        foreach (var columnName in Columns.Keys)
        {
            if (columnName == "id")
            {
                throw new Exception("An id column is automatically added, custom id columns are not supported");
            }

            if (InvalidSQLCharacters.IsMatch(columnName))
            {
                throw new Exception($"Invalid characters in column name: {columnName}");
            }

            columnNames.Add(columnName);
        }

        foreach (var kvp in Indexes)
        {
            var indexName = kvp.Key;
            var indexColumns = kvp.Value;

            if (InvalidSQLCharacters.IsMatch(indexName))
            {
                throw new Exception($"Invalid characters in index name: {indexName}");
            }

            foreach (var indexColumn in indexColumns)
            {
                if (!columnNames.Contains(indexColumn))
                {
                    throw new Exception($"Column {indexColumn} not found for index {indexName}");
                }
            }
        }
    }

    public string ToJSON(string Name = "")
    {
        var trackPrevious = Options.TrackPreviousValues;

        var jsonObject = new
        {
            view_name = Options.ViewName ?? Name,
            local_only = Options.LocalOnly,
            insert_only = Options.InsertOnly,
            columns = ColumnsJSON.Select(c => c.ToJSONObject()).ToList(),
            indexes = IndexesJSON.Select(i => i.ToJSONObject(this)).ToList(),

            include_metadata = Options.TrackMetadata,
            ignore_empty_update = Options.IgnoreEmptyUpdates,
            include_old = (object)(trackPrevious switch
            {
                null => false,
                { Columns: null } => true,
                { Columns: var cols } => cols
            }),
            include_old_only_when_changed = trackPrevious?.OnlyWhenChanged ?? false
        };

        return JsonConvert.SerializeObject(jsonObject);
    }
}
