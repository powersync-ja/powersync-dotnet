namespace PowerSync.Common.Tests.Models;

using PowerSync.Common.DB.Schema;
using PowerSync.Common.DB.Schema.Attributes;

[Table("customers")]
public class Customer
{
    [Column("id")]
    public string CustomerId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("email")]
    public string Email { get; set; }
}
