namespace PowerSync.Common.DB.SchemaExample;

using PowerSync.Common.DB.Schema;

using System.Collections;

public class TableSchema
{
    public string Name { get; }
    public IReadOnlyDictionary<string, ColumnType> Columns { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Indexes { get; }
    public bool LocalOnly { get; }

    internal TableSchema(TableSchemaBuilder builder)
    {
        Name = builder.Name;
        Columns = new Dictionary<string, ColumnType>(builder.Columns.ToDictionary());
        Indexes = builder.Indexes
            .ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value.ToList());
        LocalOnly = builder.LocalOnly;
    }
}

public class TableSchemaBuilder
{
    public string Name { get; set; } = null!;
    public ColumnCollection Columns { get; } = new();
    public IndexCollection Indexes { get; } = new();
    public bool LocalOnly { get; set; }

    public TableSchema Build() => new TableSchema(this);
}

public class ColumnCollection : IEnumerable<KeyValuePair<string, ColumnType>>
{
    private readonly Dictionary<string, ColumnType> _columns = new();

    public ColumnType this[string name]
    {
        get => _columns[name];
        set => _columns[name] = value;
    }

    public Dictionary<string, ColumnType> ToDictionary() => new(_columns);
    public IEnumerator<KeyValuePair<string, ColumnType>> GetEnumerator() => _columns.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class IndexCollection : IEnumerable<KeyValuePair<string, Index>>
{
    private readonly Dictionary<string, Index> _indexes = new();

    public Index this[string name]
    {
        get => _indexes[name];
        set => _indexes[name] = value;
    }

    public Dictionary<string, Index> ToDictionary() => new(_indexes);
    public IEnumerator<KeyValuePair<string, Index>> GetEnumerator() => _indexes.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class Index : IEnumerable<string>
{
    private readonly List<string> _columns = new();

    public void Add(string column) => _columns.Add(column);
    public List<string> ToList() => new(_columns);
    public IEnumerator<string> GetEnumerator() => _columns.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}