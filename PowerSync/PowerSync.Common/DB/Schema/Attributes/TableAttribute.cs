namespace PowerSync.Common.DB.Schema.Attributes;

[Flags]
public enum TrackPrevious
{
    None = 0,
    Table = 1 << 0,
    Columns = 1 << 1,
    OnlyWhenChanged = 1 << 2,
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class TableAttribute : Attribute
{
    public string Name { get; }
    public bool LocalOnly { get; set; }
    public bool InsertOnly { get; set; }
    public string? ViewName { get; set; }
    public bool TrackMetadata { get; set; }
    public bool IgnoreEmptyUpdates { get; set; }
    public TrackPrevious TrackPreviousValues { get; set; }

    public TableAttribute(string name)
    {
        Name = name;
    }
}
