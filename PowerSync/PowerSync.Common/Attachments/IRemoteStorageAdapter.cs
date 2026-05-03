namespace PowerSync.Common.Attachments;

/// <summary>
/// Remote storage operations used by <see cref="AttachmentQueue"/>.
/// Implementations handle uploading, downloading, and deleting files from remote storage.
/// </summary>
/// <remarks>
/// All operations must be idempotent — partial-success retries will replay them, and a missing
/// file must not throw.
/// </remarks>
public interface IRemoteStorageAdapter
{
    /// <summary>
    /// Uploads a file to remote storage from a stream.
    /// </summary>
    /// <param name="fileData">Stream of bytes to upload.</param>
    /// <param name="attachment">The associated attachment metadata.</param>
    /// <returns>A task that completes when the upload is finished.</returns>
    /// <remarks>The caller owns and disposes the stream.</remarks>
    Task UploadFileAsync(Stream fileData, Attachment attachment);

    /// <summary>
    /// Downloads a file from remote storage as a stream.
    /// </summary>
    /// <param name="attachment">The attachment describing the file to download.</param>
    /// <returns>A readable stream over the downloaded file's contents.</returns>
    /// <remarks>The caller disposes the returned stream.</remarks>
    Task<Stream> DownloadFileAsync(Attachment attachment);

    /// <summary>
    /// Deletes a file from remote storage.
    /// </summary>
    /// <param name="attachment">The attachment describing the file to delete.</param>
    /// <returns>A task that completes when the file has been deleted.</returns>
    Task DeleteFileAsync(Attachment attachment);
}
