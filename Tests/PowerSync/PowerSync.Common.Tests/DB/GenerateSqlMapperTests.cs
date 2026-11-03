namespace PowerSync.Common.Tests.DB;

using PowerSync.Common.Client;
using PowerSync.Common.DB;
using PowerSync.Common.DB.Schema.Attributes;
using PowerSync.Common.Tests.Utils;

public enum Priority
{
    Low = 0,
    High = 2,
}

[GenerateSqlMapper]
public partial class MappedTypes
{
    public string Text { get; set; } = "";
    public string? NullText { get; set; }
    public int Int { get; set; }
    public long Long { get; set; }
    public short Short { get; set; }
    public byte Byte { get; set; }
    public bool Bool { get; set; }
    public double Double { get; set; }
    public float Float { get; set; }
    public decimal Decimal { get; set; }
    public DateTime DateTime { get; set; }
    public DateTimeOffset DateTimeOffset { get; set; }
    public Guid Guid { get; set; }
    public Priority Priority { get; set; }
    public byte[] Blob { get; set; } = [];
    public int? NullInt { get; set; }
    public decimal? NullDecimal { get; set; }
    public Guid? NullGuid { get; set; }
    public Priority? NullPriority { get; set; }
    public byte[]? NullBlob { get; set; }
}

[GenerateSqlMapper]
public partial class ColumnAttributeType
{
    [Column("id")]
    public string TodoId { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Ignored]
    public string NotAColumn { get; set; } = "default";

    public int Computed => 1;
}

[GenerateSqlMapper]
public partial record PositionalRecord(string Id, [property: Column("todo_count")] long TodoCount)
{
    public string? Note { get; init; }
}

[GenerateSqlMapper]
public readonly partial record struct PositionalRecordStruct(string Id, double Value);

[GenerateSqlMapper]
public partial class RequiredMembers
{
    public required string Id { get; init; }
    public string Name { get; private set; } = "";
}

public class BaseRow
{
    public string Id { get; set; } = "";
}

[GenerateSqlMapper]
public partial class DerivedRow : BaseRow
{
    public string Name { get; set; } = "";
}

[GenerateSqlMapper]
public partial class ConstructorAndProperties(string id)
{
    public string Id { get; } = id;
    public string Name { get; set; } = "";
}

public partial class Outer
{
    [GenerateSqlMapper]
    public partial class Nested
    {
        public string Id { get; set; } = "";
    }
}

/// <summary>
/// dotnet test -v n --framework net10.0 --filter "GenerateSqlMapperTests"
/// </summary>
[Collection("GenerateSqlMapperTests")]
public class GenerateSqlMapperTests : IAsyncLifetime
{
    private PowerSyncDatabase db = default!;
    private string dbName = default!;

    public async Task InitializeAsync()
    {
        dbName = $"GenerateSqlMapper-{Guid.NewGuid():N}.db";
        db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = dbName },
            Schema = TestSchema.AppSchema,
        });
        await db.Init();
    }

    public async Task DisposeAsync()
    {
        await db.Close();
        DatabaseUtils.CleanDb(dbName);
    }

    [Fact]
    public async Task MapsSupportedTypes()
    {
        var guid = Guid.NewGuid();
        var result = await db.Get<MappedTypes>(
            """
            SELECT
                'hello' AS Text, NULL AS NullText,
                1 AS Int, 9000000000 AS Long, -3 AS Short, 255 AS Byte, 1 AS Bool,
                1.5 AS Double, 2.5 AS Float, '12.345' AS Decimal,
                '2024-01-02T03:04:05.0000000Z' AS DateTime, '2024-01-02T03:04:05+02:00' AS DateTimeOffset,
                ? AS Guid, 2 AS Priority, x'0102' AS Blob,
                NULL AS NullInt, NULL AS NullDecimal, NULL AS NullGuid, NULL AS NullPriority, NULL AS NullBlob
            """,
            [guid.ToString()]);

        Assert.Equal("hello", result.Text);
        Assert.Null(result.NullText);
        Assert.Equal(1, result.Int);
        Assert.Equal(9000000000, result.Long);
        Assert.Equal(-3, result.Short);
        Assert.Equal(255, result.Byte);
        Assert.True(result.Bool);
        Assert.Equal(1.5, result.Double);
        Assert.Equal(2.5f, result.Float);
        Assert.Equal(12.345m, result.Decimal);
        Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), result.DateTime);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), result.DateTimeOffset);
        Assert.Equal(guid, result.Guid);
        Assert.Equal(Priority.High, result.Priority);
        Assert.Equal(new byte[] { 1, 2 }, result.Blob);
        Assert.Null(result.NullInt);
        Assert.Null(result.NullDecimal);
        Assert.Null(result.NullGuid);
        Assert.Null(result.NullPriority);
        Assert.Null(result.NullBlob);
    }

    [Fact]
    public async Task NullableMembersReadValues()
    {
        var guid = Guid.NewGuid();
        var result = await db.Get<MappedTypes>(
            """
            SELECT
                '' AS Text, 'x' AS NullText, 0 AS Int, 0 AS Long, 0 AS Short, 0 AS Byte, 0 AS Bool,
                0 AS Double, 0 AS Float, 0 AS Decimal, '2024-01-01' AS DateTime, '2024-01-01' AS DateTimeOffset,
                ? AS Guid, 0 AS Priority, x'' AS Blob,
                7 AS NullInt, 1.25 AS NullDecimal, ? AS NullGuid, 2 AS NullPriority, x'03' AS NullBlob
            """,
            [guid.ToString(), guid.ToString()]);

        Assert.Equal("x", result.NullText);
        Assert.Equal(7, result.NullInt);
        Assert.Equal(1.25m, result.NullDecimal);
        Assert.Equal(guid, result.NullGuid);
        Assert.Equal(Priority.High, result.NullPriority);
        Assert.Equal(new byte[] { 3 }, result.NullBlob);
    }

    [Fact]
    public async Task UsesColumnAttributeAndSkipsIgnoredProperties()
    {
        var result = await db.Get<ColumnAttributeType>("SELECT 'a' AS id, '2024-01-01T00:00:00Z' AS created_at");

        Assert.Equal("a", result.TodoId);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), result.CreatedAt.ToUniversalTime());
        Assert.Equal("default", result.NotAColumn);
    }

    [Fact]
    public async Task MapsPositionalRecords()
    {
        var record = await db.Get<PositionalRecord>("SELECT 'a' AS id, 3 AS todo_count, 'n' AS note");
        Assert.Equal(new PositionalRecord("a", 3) { Note = "n" }, record);

        var recordStruct = await db.Get<PositionalRecordStruct>("SELECT 'b' AS id, 1.5 AS value");
        Assert.Equal(new PositionalRecordStruct("b", 1.5), recordStruct);
    }

    [Fact]
    public async Task MapsRequiredInheritedAndConstructorMembers()
    {
        var required = await db.Get<RequiredMembers>("SELECT 'a' AS id, 'name' AS name");
        Assert.Equal("a", required.Id);
        Assert.Equal("name", required.Name);

        var derived = await db.Get<DerivedRow>("SELECT 'b' AS id, 'name' AS name");
        Assert.Equal("b", derived.Id);
        Assert.Equal("name", derived.Name);

        var constructed = await db.Get<ConstructorAndProperties>("SELECT 'c' AS id, 'name' AS name");
        Assert.Equal("c", constructed.Id);
        Assert.Equal("name", constructed.Name);

        var nested = await db.Get<Outer.Nested>("SELECT 'd' AS id");
        Assert.Equal("d", nested.Id);
    }

    [Fact]
    public async Task ThrowsOnNullForNonNullableMember()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Get<ColumnAttributeType>("SELECT NULL AS id, '2024-01-01' AS created_at"));
    }

    [Fact]
    public async Task ThrowsOnMissingColumn()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => db.Get<ColumnAttributeType>("SELECT 'a' AS id"));
    }

    [Fact]
    public async Task WorksWithGetAllAndWatch()
    {
        var all = await db.GetAll<PositionalRecord>("SELECT 'a' AS id, 1 AS todo_count, NULL AS note UNION ALL SELECT 'b', 2, NULL");
        Assert.Equal(["a", "b"], all.Select(r => r.Id));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var result in db.Watch<PositionalRecord>("SELECT 'a' AS id, 1 AS todo_count, NULL AS note", null, new() { Signal = cts.Token }))
        {
            Assert.Equal("a", Assert.Single(result).Id);
            break;
        }
    }
}
