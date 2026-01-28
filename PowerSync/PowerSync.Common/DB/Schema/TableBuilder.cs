namespace PowerSync.Common.DB.Schema;

using System.Collections;

public class TableBuilder()
{
    public ColumnMap Columns { get; set; } = new();
    public IndexMap Indexes { get; set; } = new();

    public string Name { get; set; } = null!;
    public bool LocalOnly { get; set; } = false;
    public bool InsertOnly { get; set; } = false;
    string? ViewName { get; set; }
    bool? TrackMetadata { get; set; }
    TrackPreviousOptions? TrackPreviousValues { get; set; }
    bool? IgnoreEmptyUpdates { get; set; }

    public Table Build()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new Exception("Table name is required.");
        }
        TableOptions options = new(
            indexes: Indexes.Indexes,
            localOnly: LocalOnly,
            insertOnly: InsertOnly,
            viewName: ViewName,
            trackMetadata: TrackMetadata,
            trackPreviousValues: TrackPreviousValues,
            ignoreEmptyUpdates: IgnoreEmptyUpdates
        );
        return new Table(Name, Columns.Columns, options);
    }
}

public class ColumnMap : IEnumerable
{
    public Dictionary<string, ColumnType> Columns { get; } = new();

    public ColumnType this[string name] { set { Columns[name] = value; } }
    public IEnumerator GetEnumerator() => Columns.GetEnumerator();
}

public class IndexMap : IEnumerable
{
    public Dictionary<string, List<string>> Indexes { get; } = new();

    public List<string> this[string name] { set { Indexes[name] = value; } }
    public IEnumerator GetEnumerator() => Indexes.GetEnumerator();
}

