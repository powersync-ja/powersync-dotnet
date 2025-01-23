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

    // Represents the mapping of column names to their types
    public Dictionary<string, ColumnType> MappedColumns;

    public Table(Dictionary<string, ColumnType> columns, TableOptions? options = null)
    {
        // Convert columns to a list of Column objects
        var convertedColumns = columns
            .Select(kv => new Column(new ColumnOptions(kv.Key, kv.Value)))
            .ToList();

        // Convert indexes to a list of Index objects
        var convertedIndexes = (Options?.Indexes ?? new Dictionary<string, List<string>>())
            .Select(kv =>
                new Index(new IndexOptions(
                    kv.Key,
                    kv.Value.Select(name =>
                        new IndexedColumn(new IndexColumnOptions(
                            name.Replace("-", ""), !name.StartsWith("-")))
                    ).ToList()
                ))
            )
            .ToList();

        // Initialize options
        Options ??= new TableOptions();

        MappedColumns = columns;
    }
}

