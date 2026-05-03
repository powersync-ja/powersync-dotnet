namespace PowerSync.Common.Attachments;

/// <summary>
/// Represents the current synchronization state of an attachment.
/// </summary>
public enum AttachmentState
{
    /// <summary>Attachment to be uploaded.</summary>
    QueuedUpload = 0,

    /// <summary>Attachment to be downloaded.</summary>
    QueuedDownload = 1,

    /// <summary>Attachment to be deleted.</summary>
    QueuedDelete = 2,

    /// <summary>Attachment has been synced.</summary>
    Synced = 3,

    /// <summary>Attachment has been orphaned, i.e. the associated record has been deleted.</summary>
    Archived = 4,
}
