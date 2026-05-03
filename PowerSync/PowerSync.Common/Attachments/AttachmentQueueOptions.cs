namespace PowerSync.Common.Attachments;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PowerSync.Common.Client;

/// <summary>
/// Options for configuring an <see cref="AttachmentQueue"/> instance.
/// </summary>
public sealed class AttachmentQueueOptions
{
    /// <summary>The PowerSync database instance.</summary>
    public PowerSyncDatabase Db { get; init; } = default!;

    /// <summary>Local storage adapter for file persistence.</summary>
    public ILocalStorageAdapter LocalStorage { get; init; } = default!;

    /// <summary>Remote storage adapter for upload/download/delete operations.</summary>
    public IRemoteStorageAdapter RemoteStorage { get; init; } = default!;

    /// <summary>Callback for monitoring attachment changes in your data model.</summary>
    public Func<CancellationToken, IAsyncEnumerable<WatchedAttachmentItem[]>> WatchAttachments { get; init; } = default!;

    /// <summary>Optional logger. Default: <see cref="NullLogger.Instance"/>.</summary>
    public ILogger? Logger { get; init; } = NullLogger.Instance;

    /// <summary>Name of the attachment table. Default: <see cref="Attachment.TableName"/>.</summary>
    public string TableName { get; init; } = Attachment.TableName;

    /// <summary>Periodic polling interval used to retry failed transfers. Default: 30 seconds.</summary>
    public TimeSpan SyncInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Minimum gap between consecutive sync passes. Coalesces bursts of triggers into one pass.
    /// Default: 30 milliseconds.
    /// </summary>
    public TimeSpan SyncThrottle { get; init; } = TimeSpan.FromMilliseconds(30);

    /// <summary>Whether to automatically download remote attachments. Default: <c>true</c>.</summary>
    public bool DownloadAttachments { get; init; } = true;

    /// <summary>Maximum number of archived attachments to keep before cleanup. Default: 100.</summary>
    public int ArchivedCacheLimit { get; init; } = 100;

    /// <summary>
    /// Optional error handler controlling retry/archive decisions.
    /// When unset, transient failures retry forever.
    /// </summary>
    public IAttachmentErrorHandler? ErrorHandler { get; init; }
}
