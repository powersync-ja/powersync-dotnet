namespace PowerSync.Common.DB.Schema;

class IndexedColumnJSONOptions(string Name, bool Ascending = true)
{
    public string Name { get; set; } = Name;
    public bool Ascending { get; set; } = Ascending;
}

class IndexedColumnJSON(IndexedColumnJSONOptions options)
{
    public string Name { get; set; } = options.Name;

    public bool Ascending { get; set; } = options.Ascending;

    public object ToJSONObject(CompiledTable parentTable)
    {
        var colType = parentTable.Columns.TryGetValue(Name, out var value) ? value : default;

        return new
        {
            name = Name,
            ascending = Ascending,
            type = colType.ToString()
        };
    }
}
