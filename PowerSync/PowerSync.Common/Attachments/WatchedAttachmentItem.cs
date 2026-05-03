namespace PowerSync.Common.Attachments;

/// <summary>
/// An attachment reference in your application's data model.
/// </summary>
/// <remarks>
/// Provide either <see cref="Filename"/> or <see cref="FileExtension"/> (not both).
/// When only <see cref="FileExtension"/> is provided, the queue derives a filename of the form <c>{Id}.{FileExtension}</c>.
/// </remarks>
public sealed record WatchedAttachmentItem
{
    /// <summary>
    /// Initializes a new <see cref="WatchedAttachmentItem"/>.
    /// </summary>
    /// <param name="id">Attachment record ID.</param>
    /// <param name="filename">Filename to store the attachment with. Mutually exclusive with <paramref name="fileExtension"/>.</param>
    /// <param name="fileExtension">File extension used to derive an internal filename when <paramref name="filename"/> is not provided.</param>
    /// <param name="metaData">Optional metadata.</param>
    /// <exception cref="ArgumentException">Thrown when neither or both of <paramref name="filename"/> and <paramref name="fileExtension"/> are provided.</exception>
    public WatchedAttachmentItem(string id, string? filename = null, string? fileExtension = null, string? metaData = null)
    {
        if (filename is null && fileExtension is null)
        {
            throw new ArgumentException("Either filename or fileExtension must be provided.");
        }

        if (filename is not null && fileExtension is not null)
        {
            throw new ArgumentException("Only one of filename or fileExtension may be provided.");
        }

        Id = id;
        Filename = filename;
        FileExtension = fileExtension;
        MetaData = metaData;
    }

    /// <summary>Gets the attachment record ID.</summary>
    public string Id { get; }

    /// <summary>Gets the filename, or <c>null</c> when <see cref="FileExtension"/> is set.</summary>
    public string? Filename { get; }

    /// <summary>Gets the file extension, or <c>null</c> when <see cref="Filename"/> is set.</summary>
    public string? FileExtension { get; }

    /// <summary>Gets optional metadata for the attachment.</summary>
    public string? MetaData { get; }
}
