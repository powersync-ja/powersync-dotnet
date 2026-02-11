namespace PowerSync.Common.Tests.DB.Schema;

using Newtonsoft.Json;

using PowerSync.Common.DB.Schema;
using PowerSync.Common.DB.Schema.Attributes;
using PowerSync.Common.Tests;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "SchemaTests"
/// </summary>
public class SchemaTests
{
    private void TestParser(Type type, CompiledTable expected)
    {
        var parser = new AttributeParser(type);
        var table = parser.ParseTable().Compile();
        Assert.Equivalent(expected, table, strict: true);
    }

    [
        Table(
            "test_assets",
            ViewName = "test_assets_viewname",
            IgnoreEmptyUpdates = true
        ),
        Index("makemodel", ["make", "model"]),
        Index("quantity", ["quantity"]),
    ]
    class Asset
    {
        public string id { get; set; }

        public DateTime created_at { get; set; }

        public string make { get; set; }

        public string model { get; set; }

        public int quantity { get; set; }

        public string? description { get; set; }

        [Ignored]
        public string non_table_field { get; set; }
    }

    [Fact]
    public void AttributeParser_Assets_Test()
    {
        var expected = new CompiledTable(
            "test_assets",
            new Dictionary<string, ColumnType>
            {
                ["created_at"] = ColumnType.Text,
                ["make"] = ColumnType.Text,
                ["model"] = ColumnType.Text,
                ["quantity"] = ColumnType.Integer,
                ["description"] = ColumnType.Text,
            },
            new TableOptions
            {
                Indexes = new Dictionary<string, List<string>>
                {
                    ["makemodel"] = ["make", "model"],
                    ["quantity"] = ["quantity"],
                },
                LocalOnly = false,
                InsertOnly = false,
                ViewName = "test_assets_viewname",
                TrackMetadata = true,
                TrackPreviousValues = null,
                IgnoreEmptyUpdates = true,
            }
        );

        TestParser(typeof(Asset), expected);
    }

    [
        Table(
            "test_products",
            TrackMetadata = true,
            TrackPreviousValues = TrackPrevious.Columns | TrackPrevious.OnlyWhenChanged
        ),
        Index("seller_id_idx", ["seller_id"]),
    ]
    class Product
    {
        public string id { get; set; }

        [Column(ColumnType = ColumnType.Real)]
        public DateTime created_at { get; set; }

        public string description { get; set; }

        [Column(TrackPrevious = true)]
        public int quantity { get; set; }

        [Column(TrackPrevious = true)]
        public decimal ppu { get; set; }

        public string seller_id { get; set; }
    }

    [Fact]
    public void AttributeParser_Products_Test()
    {
        var expected = new CompiledTable(
            "test_products",
            new Dictionary<string, ColumnType>
            {
                ["created_at"] = ColumnType.Real, // Explicit override
                ["description"] = ColumnType.Text,
                ["quantity"] = ColumnType.Integer,
                ["ppu"] = ColumnType.Text, // 'decimal' should cast to Text for lossless conversion
                ["seller_id"] = ColumnType.Text,
            },
            new TableOptions
            {
                Indexes = new Dictionary<string, List<string>>
                {
                    ["seller_id_idx"] = ["seller_id"],
                },
                LocalOnly = false,
                InsertOnly = false,
                ViewName = null,
                TrackMetadata = true,
                TrackPreviousValues = new TrackPreviousOptions
                {
                    Columns = ["quantity", "ppu"],
                    OnlyWhenChanged = true,
                },
                IgnoreEmptyUpdates = false,
            }
        );

        TestParser(typeof(Product), expected);
    }

    enum LogLevel
    {
        Info,
        Debug,
        Warning,
        Error
    }

    [
        Table(
            "test_logs",
            LocalOnly = true,
            InsertOnly = true,
            ViewName = "logs_local",
            IgnoreEmptyUpdates = true
        )
    ]
    class Log
    {
        [Column("id")]
        public string LogId { get; set; }

        [Column("description")]
        public string Description { get; set; }

        [Column("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        [Column("log_level")]
        public LogLevel LogLevel { get; set; }

        [Ignored]
        public string LogLevelString { get { return LogLevel.ToString(); } }
    }

    [Fact]
    public void AttributeParser_Logs_Test()
    {
        var expected = new CompiledTable(
            "test_logs",
            new Dictionary<string, ColumnType>
            {
                ["description"] = ColumnType.Text,
                ["timestamp"] = ColumnType.Text,
                ["log_level"] = ColumnType.Integer,
            },
            new TableOptions
            {
                Indexes = new Dictionary<string, List<string>>(),
                LocalOnly = true,
                InsertOnly = true,
                ViewName = "logs_local",
                TrackMetadata = false,
                TrackPreviousValues = null,
                IgnoreEmptyUpdates = true,
            }
        );

        TestParser(typeof(Log), expected);
    }

    class Invalid1 { public string id { get; set; } }
    [Fact]
    public async void AttributeParser_InvalidSchema_1()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            new AttributeParser(typeof(Invalid1)).ParseTable();
        });
        Assert.Contains("must be marked with TableAttribute", ex.Message);
    }

    [Table("invalid")]
    class Invalid2 { }
    [Fact]
    public async void AttributeParser_InvalidSchema_2()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            new AttributeParser(typeof(Invalid2)).ParseTable();
        });
        Assert.Contains("'id' property is required", ex.Message);
    }

    [Table("invalid")]
    class Invalid3 { public int id { get; set; } }
    [Fact]
    public async void AttributeParser_InvalidSchema_3()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            new AttributeParser(typeof(Invalid3)).ParseTable();
        });
        Assert.Contains("must be of type string", ex.Message);
    }

    [Table("invalid")]
    class Invalid4
    {
        [Column(ColumnType = ColumnType.Real)]
        public string id { get; set; }
    }
    [Fact]
    public async void AttributeParser_InvalidSchema_4()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            new AttributeParser(typeof(Invalid4)).ParseTable();
        });
        Assert.Contains("must have ColumnType set to ColumnType.Text or ColumnType.Inferred", ex.Message);
    }

    [Table("invalid")]
    class Invalid5
    {
        public string id { get; set; }
        public Invalid1 invalid_type { get; set; }
    }
    [Fact]
    public async void AttributeParser_InvalidSchema_5()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            new AttributeParser(typeof(Invalid5)).ParseTable();
        });
        Assert.Contains("Unable to automatically infer ColumnType", ex.Message);
    }

    [Table("invalid", TrackPreviousValues = TrackPrevious.Columns | TrackPrevious.Table)]
    class Invalid6
    {
        public string id { get; set; }
    }
    [Fact]
    public async void AttributeParser_InvalidSchema_6()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            new AttributeParser(typeof(Invalid6)).ParseTable();
        });
        Assert.Contains("Cannot specify both TrackPrevious.Columns and TrackPrevious.Table", ex.Message);
    }

    [Table("invalid", TrackPreviousValues = TrackPrevious.OnlyWhenChanged)]
    class Invalid7
    {
        public string id { get; set; }
    }
    [Fact]
    public async void AttributeParser_InvalidSchema_7()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            new AttributeParser(typeof(Invalid7)).ParseTable();
        });
        Assert.Contains("Cannot specify TrackPrevious.OnlyWhenChanged without also specifying", ex.Message);
    }

    [Fact]
    public async void AttributeParser_TypeMap_CustomRegistered()
    {
        // Log has Column aliases
        new AttributeParser(typeof(Log)).RegisterDapperTypeMap();
        var typeMap = Dapper.SqlMapper.GetTypeMap(typeof(Log));
        Assert.False(typeMap is Dapper.DefaultTypeMap);
    }

    [Fact]
    public async void AttributeParser_TypeMap_DefaultRegistered()
    {
        // Asset has no Column aliases
        new AttributeParser(typeof(Asset)).RegisterDapperTypeMap();
        var typeMap = Dapper.SqlMapper.GetTypeMap(typeof(Asset));
        Assert.True(typeMap is Dapper.DefaultTypeMap);
    }

    [Fact]
    public void CompiledSchema_ToJSON()
    {
        object expectedJson = new
        {
            tables = new List<object>
            {
                new
                {
                    name = "todos",
                    view_name = "todos",
                    local_only = false,
                    insert_only = false,
                    columns = new List<object> {
                        new { name = "list_id", type = "Text" },
                        new { name = "created_at", type = "Text" },
                        new { name = "completed_at", type = "Text" },
                        new { name = "description", type = "Text" },
                        new { name = "created_by", type = "Text" },
                        new { name = "completed_by", type = "Text" },
                        new { name = "completed", type = "Integer" },
                    },
                    indexes = new List<object> {
                        new {
                            name = "list",
                            columns = new List<object> {
                                new { name = "list_id", ascending = true, type = "Text" },
                            }
                        },
                        new {
                            name = "list_rev",
                            columns = new List<object> {
                                new { name = "list_id", ascending = false, type = "Text" },
                            }
                        }
                    },
                    include_metadata = false,
                    ignore_empty_update = false,
                    include_old = false,
                    include_old_only_when_changed = false
                },
                new
                {
                    name = "lists",
                    view_name = "lists",
                    local_only = false,
                    insert_only = false,
                    columns = new List<object> {
                        new { name = "created_at", type = "Text" },
                        new { name = "name", type = "Text" },
                        new { name = "owner_id", type = "Text" }
                    },
                    indexes = new List<object>(),
                    include_metadata = false,
                    ignore_empty_update = false,
                    include_old = false,
                    include_old_only_when_changed = false
                },
            }
        };
        var schema = TestSchemaTodoList.AppSchema.Compile();

        Assert.Equal(JsonConvert.SerializeObject(expectedJson), schema.ToJSON());
    }
}
