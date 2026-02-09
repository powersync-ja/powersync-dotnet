namespace PowerSync.Common.Tests.DB.Schema;

using PowerSync.Common.Tests.Utils;
using PowerSync.Common.DB.Schema;
using PowerSync.Common.DB.Schema.Attributes;

using System.Diagnostics;

using PowerSync.Common.Client;

// TODO: Schema comparer - or can we re-use DeepEquals utility?

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "SchemaTests"
/// </summary>
public class SchemaTests
{
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

        public string description { get; set; }
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
        public string created_at { get; set; }

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

    [
        Table(
            "test_logs",
            LocalOnly = true,
            InsertOnly = true,
            ViewName = "logs_local",
            IgnoreEmptyUpdates = true
        )
    ]
    class Logs
    {
        public string id { get; set; }
        public string description { get; set; }
        public DateTimeOffset timestamp { get; set; }
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

        TestParser(typeof(Logs), expected);
    }

    private void TestParser(Type type, CompiledTable expected)
    {
        var parser = new AttributeParser(type);
        var table = parser.ParseTable().Compile();
        Assert.Equivalent(expected, table, strict: true);
    }
}
