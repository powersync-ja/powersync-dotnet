namespace PowerSync.Common.DB.Schema.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ColumnAttribute : Attribute
{
    public string? Name { get; set; } = "";
    public ColumnType ColumnType { get; set; } = ColumnType.Inferred;
    public bool TrackPrevious { get; set; }

    public ColumnAttribute(string? name = null)
    {
        Name = name;
    }
}
