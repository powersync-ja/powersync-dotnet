namespace PowerSync.Common.Attachments;

/// <summary>
/// Provides custom error handling for attachment sync operations.
/// Implementations determine whether failed operations should be retried or archived.
/// </summary>
public interface IAttachmentErrorHandler
{
    /// <summary>
    /// Handles a download error for a specific attachment.
    /// </summary>
    /// <param name="attachment">The attachment that failed to download.</param>
    /// <param name="error">The error encountered during the download.</param>
    /// <returns>A task that completes with <c>true</c> to retry the operation, or <c>false</c> to archive the attachment.</returns>
    Task<bool> OnDownloadErrorAsync(Attachment attachment, Exception error);

    /// <summary>
    /// Handles an upload error for a specific attachment.
    /// </summary>
    /// <param name="attachment">The attachment that failed to upload.</param>
    /// <param name="error">The error encountered during the upload.</param>
    /// <returns>A task that completes with <c>true</c> to retry the operation, or <c>false</c> to archive the attachment.</returns>
    Task<bool> OnUploadErrorAsync(Attachment attachment, Exception error);

    /// <summary>
    /// Handles a delete error for a specific attachment.
    /// </summary>
    /// <param name="attachment">The attachment that failed to delete.</param>
    /// <param name="error">The error encountered during the delete.</param>
    /// <returns>A task that completes with <c>true</c> to retry the operation, or <c>false</c> to archive the attachment.</returns>
    Task<bool> OnDeleteErrorAsync(Attachment attachment, Exception error);
}
