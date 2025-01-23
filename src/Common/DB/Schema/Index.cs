namespace Common.DB.Schema;

using Newtonsoft.Json;

public class IndexOptions(string name, List<IndexedColumn>? columns = null)
{
    public string Name { get; set; } = name;
    public List<IndexedColumn>? Columns { get; set; } = columns ?? new List<IndexedColumn>();
}

public class Index(IndexOptions options)
{
    public string Name { get; set; } = options.Name;

    public List<IndexedColumn> Columns => options.Columns ?? [];

    public object ToJson(Table table)
    {
        return JsonConvert.SerializeObject(new
        {
            name = Name,
            columns = Columns.Select(column => column.ToJson(table)).ToList()
        });
    }
}