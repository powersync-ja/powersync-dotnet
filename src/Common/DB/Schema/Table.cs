namespace Common.DB.Schema;

using Newtonsoft.Json;

// Need to port this to C#
// export const InvalidSQLCharacters = /["'%,.#\s[\]]/;


public class TableOptions(
    Dictionary<string, List<string>>? indexes = null,
    bool? localOnly = null,
    bool? insertOnly = null,
    string? viewName = null)
{
    public Dictionary<string, List<string>> Indexes { get; set; } = indexes ?? [];

    public bool LocalOnly { get; set; } = localOnly ?? false;

    public bool InsertOnly { get; set; } = insertOnly ?? false;

    public string? ViewName { get; set; } = viewName;
}

public class Table
{
    protected TableOptions Options { get; set; }

    public Dictionary<string, ColumnType> OriginalColumns;

    private readonly List<Column> ConvertedColumns;
    private readonly List<Index> ConvertedIndexes;

    public Table(Dictionary<string, ColumnType> columns, TableOptions? options = null)
    {
        ConvertedColumns = [.. columns.Select(kv => new Column(new ColumnOptions(kv.Key, kv.Value)))];

        ConvertedIndexes = [.. (Options?.Indexes ?? [])
            .Select(kv =>
                new Index(new IndexOptions(
                    kv.Key,
                    [.. kv.Value.Select(name =>
                        new IndexedColumn(new IndexColumnOptions(
                            name.Replace("-", ""), !name.StartsWith("-")))
                    )]
                ))
            )];

        Options = options ?? new TableOptions();

        OriginalColumns = columns;
    }

    public string ToJson(string Name = "")
    {
        var jsonObject = new
        {
            view_name = Options.ViewName ?? Name,
            local_only = Options.LocalOnly,
            insert_only = Options.InsertOnly,
            columns = ConvertedColumns.Select(c => JsonConvert.DeserializeObject<object>(c.ToJson())).ToList(),
            indexes = ConvertedIndexes.Select(e => JsonConvert.DeserializeObject<object>(e.ToJson(this))).ToList()
        };

        return JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
    }
}

