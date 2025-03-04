namespace PowerSync.Common.DB.Schema;

using Newtonsoft.Json;

public class IndexColumnOptions(string Name, bool Ascending = true)
{
    public string Name { get; set; } = Name;
    public bool Ascending { get; set; } = Ascending;
}

public class IndexedColumn(IndexColumnOptions options)
{
    protected string Name { get; set; } = options.Name;

    protected bool Ascending { get; set; } = options.Ascending;


    public object ToJSON(Table table)
    {
        var colType = table.OriginalColumns.TryGetValue(Name, out var value) ? value : default;

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