namespace PowerSync.Common.Tests.Models;

using PowerSync.Common.DB.Schema;
using PowerSync.Common.DB.Schema.Attributes;

[
    Table("assets"),
    Index("makemodel", ["make", "model"])
]
public class Asset
{
    [Column("id")]
    public string AssetId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("make")]
    public string Make { get; set; }

    [Column("model")]
    public string Model { get; set; }

    [Column("serial_number")]
    public string SerialNumber { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    [Column("user_id")]
    public string UserId { get; set; }

    [Column("customer_id")]
    public string CustomerId { get; set; }

    [Column("description")]
    public string Description { get; set; }
}
