namespace PowerSync.Common.DB.Schema;

using System.Text.RegularExpressions;
using Newtonsoft.Json;

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
    public static readonly Regex InvalidSQLCharacters = new Regex(@"[""'%,.#\s\[\]]", RegexOptions.Compiled);


    protected TableOptions Options { get; init; } = null!;

    public Dictionary<string, ColumnType> Columns;
    public Dictionary<string, List<string>> Indexes;

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

        Columns = columns;
        Indexes = Options?.Indexes ?? [];
    }

    public void Validate()
    {
        if (!string.IsNullOrWhiteSpace(Options.ViewName) && InvalidSQLCharacters.IsMatch(Options.ViewName!))
        {
            throw new Exception($"Invalid characters in view name: {Options.ViewName}");
        }

        if (Columns.Count > Column.MAX_AMOUNT_OF_COLUMNS)
        {
            throw new Exception($"Table has too many columns. The maximum number of columns is {Column.MAX_AMOUNT_OF_COLUMNS}.");
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

        foreach (var (indexName, indexColumns) in Indexes)
        {

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
        var jsonObject = new
        {
            view_name = Options.ViewName ?? Name,
            local_only = Options.LocalOnly,
            insert_only = Options.InsertOnly,
            columns = ConvertedColumns.Select(c => JsonConvert.DeserializeObject<object>(c.ToJSON())).ToList(),
            indexes = ConvertedIndexes.Select(e => JsonConvert.DeserializeObject<object>(e.ToJSON(this))).ToList()
        };

        return JsonConvert.SerializeObject(jsonObject);
    }
}
