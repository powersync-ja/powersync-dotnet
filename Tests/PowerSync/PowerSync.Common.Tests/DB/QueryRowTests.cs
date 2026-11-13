namespace PowerSync.Common.Tests.DB;

using System.Globalization;

using PowerSync.Common.Client;
using PowerSync.Common.DB;
using PowerSync.Common.Tests.Utils;

/// <summary>
/// dotnet test -v n --framework net10.0 --filter "QueryRowTests"
/// </summary>
[Collection("QueryRowTests")]
public class QueryRowTests : IAsyncLifetime
{
    private const string ValuesQuery =
        """
        SELECT
            'text' AS text, 42 AS int, 2.5 AS real, 3.7 AS real_up, x'0102' AS blob, NULL AS null_col,
            '17' AS text_int, '2.25' AS text_real, 1 AS one, 0 AS zero, 'True' AS text_bool,
            '2024-05-06T07:08:09.123Z' AS iso_utc, '2024-05-06T07:08:09' AS iso_local, '2024-05-06T07:08:09+02:00' AS iso_offset,
            3000000000 AS big
        """;

    private PowerSyncDatabase db = default!;
    private string dbName = default!;

    public async Task InitializeAsync()
    {
        dbName = $"QueryRow-{Guid.NewGuid():N}.db";
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

    private static string Describe(Func<object?> read)
    {
        try
        {
            return read() switch
            {
                null => "null",
                byte[] bytes => "bytes:" + Convert.ToHexString(bytes),
                DateTime dt => $"DateTime:{dt:O}:{dt.Kind}",
                IFormattable f => $"{f.GetType().Name}:{f.ToString(null, CultureInfo.InvariantCulture)}",
                var value => $"{value.GetType().Name}:{value}",
            };
        }
        catch (Exception e)
        {
            return e.GetType().Name;
        }
    }

    private static readonly string[] Columns =
        ["text", "int", "real", "real_up", "blob", "null_col", "text_int", "text_real", "one", "zero", "text_bool", "iso_utc", "iso_local", "iso_offset", "big", "INT", "missing"];

    private static List<string> ReadAll(IQueryRow row)
    {
        var results = new List<string>();
        foreach (var column in Columns)
        {
            results.Add($"{column} value {Describe(() => row.GetValue(column))}");
            results.Add($"{column} string {Describe(() => row.GetString(column))}");
            results.Add($"{column} int {Describe(() => row.GetInt(column))}");
            results.Add($"{column} long {Describe(() => row.GetLong(column))}");
            results.Add($"{column} bool {Describe(() => row.GetBool(column))}");
            results.Add($"{column} double {Describe(() => row.GetDouble(column))}");
            results.Add($"{column} datetime {Describe(() => row.GetDateTime(column))}");
            results.Add($"{column} string? {Describe(() => row.GetStringOrNull(column))}");
            results.Add($"{column} int? {Describe(() => row.GetIntOrNull(column))}");
            results.Add($"{column} long? {Describe(() => row.GetLongOrNull(column))}");
            results.Add($"{column} bool? {Describe(() => row.GetBoolOrNull(column))}");
            results.Add($"{column} double? {Describe(() => row.GetDoubleOrNull(column))}");
            results.Add($"{column} datetime? {Describe(() => row.GetDateTimeOrNull(column))}");
        }
        return results;
    }

    [Fact]
    public async Task StreamedAndMaterializedRowsAgree()
    {
        var materialized = ReadAll(await db.Get(ValuesQuery));
        var streamed = await db.Get(ValuesQuery, null, ReadAll);
        var streamedAll = Assert.Single(await db.GetAll(ValuesQuery, null, ReadAll));

        Assert.Equal(materialized, streamed);
        Assert.Equal(materialized, streamedAll);
    }

    [Fact]
    public async Task GettersFollowConvertSemantics()
    {
        var row = await db.Get(ValuesQuery);

        Assert.Equal(["text", "int", "real", "real_up", "blob", "null_col", "text_int", "text_real", "one", "zero", "text_bool", "iso_utc", "iso_local", "iso_offset", "big"], row.Columns);
        Assert.Equal(42L, row.GetValue("int"));
        Assert.Equal(42L, row.GetLong("INT"));
        Assert.Equal("42", row.GetString("int"));
        Assert.Equal(2.5, row.GetDouble("real"));
        Assert.Equal(2L, row.GetLong("real"));
        Assert.Equal(4L, row.GetLong("real_up"));
        Assert.Equal(17L, row.GetLong("text_int"));
        Assert.Equal(2.25, row.GetDouble("text_real"));
        Assert.Equal(42.0, row.GetDouble("int"));
        Assert.True(row.GetBool("one"));
        Assert.False(row.GetBool("zero"));
        Assert.True(row.GetBool("text_bool"));
        Assert.Equal(new byte[] { 1, 2 }, row.GetValue("blob"));
        Assert.Null(row.GetValue("null_col"));
        Assert.Null(row.GetLongOrNull("null_col"));
        Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 9, 123, DateTimeKind.Utc), row.GetDateTime("iso_utc"));
        Assert.Equal(DateTimeKind.Unspecified, row.GetDateTime("iso_local").Kind);

        Assert.Throws<InvalidOperationException>(() => row.GetString("null_col"));
        Assert.Throws<ArgumentException>(() => row.GetString("missing"));
        Assert.Throws<ArgumentNullException>(() => row.GetString(null!));
        Assert.Throws<OverflowException>(() => row.GetInt("big"));
        Assert.Throws<FormatException>(() => row.GetLong("text"));
    }

    [Fact]
    public async Task LookupIsCaseInsensitiveAndFirstColumnWins()
    {
        var row = await db.Get("SELECT 1 AS dup, 2 AS DUP, 3 AS Other");
        Assert.Equal(1L, row.GetLong("dup"));
        Assert.Equal(1L, row.GetLong("DUP"));
        Assert.Equal(1L, row.GetLong(new string("Dup".ToCharArray())));
        Assert.Equal(3L, row.GetLong("other"));

        var streamed = await db.GetAll("SELECT 1 AS dup, 2 AS DUP UNION ALL SELECT 5, 6", null, r => (r.GetLong("DUP"), r.GetLong("dup")));
        Assert.Equal([(1L, 1L), (5L, 5L)], streamed);
    }

    [Fact]
    public async Task WideResultSetsResolveColumns()
    {
        var columns = Enumerable.Range(0, 40).Select(i => $"{i} AS c{i}");
        var sql = $"SELECT {string.Join(", ", columns)}";

        var row = await db.Get(sql);
        Assert.Equal(39L, row.GetLong("C39"));
        Assert.Equal(7L, await db.Get(sql, null, r => r.GetLong("c7")));
        Assert.Throws<ArgumentException>(() => row.GetLong("c40"));
    }

    [Fact]
    public async Task RowsReturnedFromMappersStayValid()
    {
        var rows = await db.GetAll("SELECT 'a' AS id UNION ALL SELECT 'b'", null, row => row);
        Assert.Equal(["a", "b"], rows.Select(r => r.GetString("id")));

        var single = await db.Get("SELECT 'c' AS id", null, row => (object)row);
        Assert.Equal("c", ((IQueryRow)single).GetString("id"));

        var optional = await db.GetOptional("SELECT 'd' AS id", null, row => row);
        Assert.Equal("d", optional!.GetString("id"));
    }

    [Fact]
    public async Task RowsCapturedByMappersFailAfterTheQuery()
    {
        var captured = await db.GetAll("SELECT 'a' AS id", null, row => new List<IQueryRow> { row });
        var row = Assert.Single(Assert.Single(captured));
        Assert.Throws<InvalidOperationException>(() => row.GetString("id"));
    }

    [Fact]
    public async Task EmptyResults()
    {
        Assert.Empty((await db.GetAll("SELECT 1 AS id WHERE 0")).Rows);
        Assert.Empty(await db.GetAll("SELECT 1 AS id WHERE 0", null, row => row.GetLong("id")));
        Assert.Null(await db.GetOptional("SELECT 1 AS id WHERE 0"));
        Assert.Null(await db.GetOptional("SELECT 'a' AS id WHERE 0", null, row => row.GetString("id")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Get("SELECT 1 AS id WHERE 0", null, row => row.GetLong("id")));
    }
}
