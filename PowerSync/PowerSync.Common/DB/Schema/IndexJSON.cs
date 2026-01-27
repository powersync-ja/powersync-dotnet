namespace PowerSync.Common.DB.Schema;

class IndexJSONOptions(string name, List<IndexedColumnJSON>? columns = null)
{
    public string Name { get; set; } = name;
    public List<IndexedColumnJSON>? Columns { get; set; } = columns ?? new List<IndexedColumnJSON>();
}

class IndexJSON(IndexJSONOptions options)
{
    public string Name { get; set; } = options.Name;

    public List<IndexedColumnJSON> Columns => options.Columns ?? [];

    public object ToJSONObject(Table table)
    {
        return new
        {
            name = Name,
            columns = Columns.Select(column => column.ToJSON(table)).ToList()
        };
    }
}
