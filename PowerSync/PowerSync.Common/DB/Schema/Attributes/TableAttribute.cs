namespace PowerSync.Common.DB.Schema.Attributes;

[Flags]
public enum TrackPrevious
{
    // TODO: Consider finding a method that can't represent invalid states (eg. TrackPrevious.Table | TrackPrevious.Columns)
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

[
    Table(
        "test",
        LocalOnly = true,
        InsertOnly = false,
        ViewName = "test_local",
        TrackPreviousValues = TrackPrevious.Columns | TrackPrevious.OnlyWhenChanged
    ),
    Index("list", [nameof(list_id)]),
    Index("completed", [nameof(completed), nameof(completed_by)])
]
public class Test
{
    public string id { get; set; }

    [Column(ColumnType.Integer)] // ColumnType overrides
    public string list_id { get; set; }

    public DateTime created_at { get; set; }

    [Column(ColumnType.Text, TrackPrevious = true)] // Column-wise track previous
    public DateTime? completed_at { get; set; }

    public string created_by { get; set; }

    public string? completed_by { get; set; }

    public bool completed { get; set; }
}
