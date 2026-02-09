namespace PowerSync.Common.DB.Schema.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ColumnAttribute : Attribute
{
    public ColumnType ColumnType { get; set; } = ColumnType.Inferred;
    public bool TrackPrevious { get; set; }
}
