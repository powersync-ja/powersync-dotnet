namespace PowerSync.Common.DB.Schema;

using Newtonsoft.Json;

public enum ColumnType
{
    TEXT,
    INTEGER,
    REAL
}

public class ColumnOptions(string Name, ColumnType? Type)
{
    public string Name { get; set; } = Name;
    public ColumnType? Type { get; set; } = Type;
}

public class Column(ColumnOptions options)
{
    public const int MAX_AMOUNT_OF_COLUMNS = 1999;

    public string Name { get; set; } = options.Name;

    public ColumnType Type { get; set; } = options.Type ?? ColumnType.TEXT;

    public string ToJSON()
    {
        return JsonConvert.SerializeObject(new
        {
            name = Name,
            type = Type.ToString()
        });
    }
}
