namespace PowerSync.Common.DB.Schema;

using System.Collections.Generic;
using System.Text.RegularExpressions;

using Newtonsoft.Json;

class CompiledTable
{
    public static readonly Regex InvalidSQLCharacters = new Regex(@"[""'%,.#\s\[\]]", RegexOptions.Compiled);

    public string Name { get; init; } = null!;
    protected TableOptions Options { get; init; } = null!;
    public IReadOnlyDictionary<string, ColumnType> Columns { get; init; }
    public IReadOnlyDictionary<string, List<string>> Indexes { get; init; }

    private readonly ColumnJSON[] ColumnsJSON;
    private readonly IndexJSON[] IndexesJSON;

    public CompiledTable(string name, Dictionary<string, ColumnType> columns, TableOptions options)
    {
        ColumnsJSON =
            columns
            .Select(kvp => new ColumnJSON(new ColumnJSONOptions(kvp.Key, kvp.Value)))
            .ToArray();

        IndexesJSON =
            (Options?.Indexes ?? [])
            .Select(kvp =>
                new IndexJSON(new IndexJSONOptions(
                    kvp.Key,
                    kvp.Value.Select(name =>
                        new IndexedColumnJSON(new IndexedColumnJSONOptions(
                            name.Replace("-", ""), !name.StartsWith("-")))
                    ).ToArray()
                ))
            )
            .ToArray();

        Name = name;
        Columns = columns;
        Options = options;
        Indexes = Options?.Indexes ?? [];
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

        if (!string.IsNullOrWhiteSpace(Options.ViewName) && InvalidSQLCharacters.IsMatch(Options.ViewName!))
        {
            throw new Exception($"Invalid characters in view name: {Options.ViewName}");
        }

        if (Columns.Count > Table.MAX_AMOUNT_OF_COLUMNS)
        {
            throw new Exception(
                $"Table has too many columns. The maximum number of columns is {Table.MAX_AMOUNT_OF_COLUMNS}.");
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
