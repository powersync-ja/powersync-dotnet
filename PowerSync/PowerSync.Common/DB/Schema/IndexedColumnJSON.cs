namespace PowerSync.Common.DB.Schema;

using Newtonsoft.Json;

class IndexedColumnJSONOptions(string Name, bool Ascending = true)
{
    public string Name { get; set; } = Name;
    public bool Ascending { get; set; } = Ascending;
}

class IndexedColumnJSON(IndexedColumnJSONOptions options)
{
    protected string Name { get; set; } = options.Name;

    protected bool Ascending { get; set; } = options.Ascending;

    public string ToJSON(CompiledTable table)
    {
        var colType = table.Columns.TryGetValue(Name, out var value) ? value : default;

        return JsonConvert.SerializeObject(
         new
         {
             name = Name,
             ascending = Ascending,
             type = colType.ToString()
         }
        );
    }
}
