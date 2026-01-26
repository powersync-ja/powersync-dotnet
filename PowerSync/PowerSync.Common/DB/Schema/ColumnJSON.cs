namespace PowerSync.Common.DB.Schema;

using Newtonsoft.Json;

public enum ColumnType
{
    Text,
    Integer,
    Real
}

class ColumnJSONOptions(string Name, ColumnType? Type)
{
    public string Name { get; set; } = Name;
    public ColumnType? Type { get; set; } = Type;
}

class ColumnJSON(ColumnJSONOptions options)
{
    public string Name { get; set; } = options.Name;

    public ColumnType Type { get; set; } = options.Type ?? ColumnType.Text;

    public string ToJSON()
    {
        return JsonConvert.SerializeObject(new
        {
            name = Name,
            type = Type.ToString()
        });
    }
}
