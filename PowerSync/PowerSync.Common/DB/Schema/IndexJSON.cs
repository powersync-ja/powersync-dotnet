namespace PowerSync.Common.DB.Schema;

class IndexJSONOptions(string name, IndexedColumnJSON[]? columns = null)
{
    public string Name { get; set; } = name;
    public IndexedColumnJSON[]? Columns { get; set; } = columns ?? [];
}

class IndexJSON(IndexJSONOptions options)
{
    public string Name { get; set; } = options.Name;

    public IndexedColumnJSON[] Columns => options.Columns ?? [];

    public object ToJSONObject(CompiledTable table)
    {
        return new
        {
            name = Name,
            columns = Columns.Select(column => column.ToJSONObject(table)).ToList()
        };
    }
}
