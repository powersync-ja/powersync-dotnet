namespace PowerSync.Common.DB.Schema.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ColumnAttribute : Attribute
{
    public ColumnType ColumnType { get; }
    public bool TrackPrevious { get; set; }

    public ColumnAttribute(ColumnType columnType)
    {
        ColumnType = columnType;
    }
}
