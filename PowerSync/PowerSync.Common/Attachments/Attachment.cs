namespace PowerSync.Common.Attachments;

using PowerSync.Common.DB.Schema.Attributes;

/// <summary>
/// An attachment record persisted in the local database.
/// </summary>
[Table(TableName, LocalOnly = true)]
public sealed class Attachment
{
    /// <summary>The attachment table name.</summary>
    public const string TableName = "attachments";

    [Column("id")]
    public string Id { get; set; } = string.Empty;

    [Column("filename")]
    public string Filename { get; set; } = string.Empty;

    [Column("state")]
    public AttachmentState State { get; set; }

    [Column("local_uri")]
    public string? LocalUri { get; set; }

    [Column("size")]
    public long? Size { get; set; }

    [Column("media_type")]
    public string? MediaType { get; set; }

    [Column("timestamp")]
    public long Timestamp { get; set; }

    [Column("meta_data")]
    public string? MetaData { get; set; }

    [Column("has_synced")]
    public bool HasSynced { get; set; }
}
