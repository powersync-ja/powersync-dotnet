namespace PowerSync.Common.DB.Schema;

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

    public object ToJSONObject()
    {
        return new
        {
            name = Name,
            type = Type.ToString()
        };
    }
}
