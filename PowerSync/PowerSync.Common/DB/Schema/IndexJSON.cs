namespace PowerSync.Common.DB.Schema;

using Newtonsoft.Json;

class IndexJSONOptions(string name, List<IndexedColumnJSON>? columns = null)
{
    public string Name { get; set; } = name;
    public List<IndexedColumnJSON>? Columns { get; set; } = columns ?? new List<IndexedColumnJSON>();
}

class IndexJSON(IndexJSONOptions options)
{
    public string Name { get; set; } = options.Name;

    public List<IndexedColumnJSON> Columns => options.Columns ?? [];

    public string ToJSON(Table table)
    {
        return JsonConvert.SerializeObject(new
        {
            name = Name,
            columns = Columns.Select(column => column.ToJSON(table)).ToList()
        });
    }
}
