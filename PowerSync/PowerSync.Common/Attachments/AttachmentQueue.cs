namespace PowerSync.Common.Attachments;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PowerSync.Common.DB;

/// <summary>
/// Manages the lifecycle and synchronization of attachments between local and remote storage.
/// Provides automatic synchronization, upload/download queuing, attachment monitoring,
/// verification and repair of local files, and cleanup of archived attachments.
/// </summary>
public sealed class AttachmentQueue : IAsyncDisposable
{
    private readonly AttachmentQueueOptions _options;
    private readonly ILogger _logger;
    private readonly AttachmentService _attachmentService;
    private readonly SyncingService _syncingService;

    private readonly SemaphoreSlim _startStopLock = new(1, 1);
    private CancellationTokenSource? _runCts;

    private Task _statusLoop = Task.CompletedTask;
    private Task _watchAttachmentsLoop = Task.CompletedTask;

    public AttachmentQueue(AttachmentQueueOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (options.Db is null)
        {
            throw new ArgumentException($"{nameof(options.Db)} is required.", nameof(options));
        }

        if (options.LocalStorage is null)
        {
            throw new ArgumentException($"{nameof(options.LocalStorage)} is required.", nameof(options));
        }

        if (options.RemoteStorage is null)
        {
            throw new ArgumentException($"{nameof(options.RemoteStorage)} is required.", nameof(options));
        }

        if (options.WatchAttachments is null)
        {
            throw new ArgumentException($"{nameof(options.WatchAttachments)} is required.", nameof(options));
        }

        _logger = options.Logger ?? NullLogger.Instance;

        _attachmentService = new AttachmentService(
            options.Db,
            options.TableName,
            options.ArchivedCacheLimit,
            _logger);

        _syncingService = new SyncingService(
            _attachmentService,
            options.LocalStorage,
            options.RemoteStorage,
            options.ErrorHandler,
            options.SyncThrottle,
            _logger);
    }

    /// <summary>
    /// Generates a new attachment ID using SQLite's <c>uuid()</c> function.
    /// Used by <see cref="SaveFileAsync"/> when the caller doesn't supply an explicit id.
    /// </summary>
    /// <returns>A task that completes with the new attachment ID.</returns>
    public Task<string> GenerateAttachmentIdAsync() => _options.Db.Get<string>("SELECT uuid()");

    /// <summary>
    /// Starts the attachment synchronization process.
    /// </summary>
    /// <remarks>
    /// This method:
    /// <list type="bullet">
    /// <item><description>Stops any existing sync operations.</description></item>
    /// <item><description>Initializes local storage and verifies attachment integrity.</description></item>
    /// <item><description>Sets up periodic synchronization based on <see cref="AttachmentQueueOptions.SyncInterval"/>.</description></item>
    /// <item><description>Registers listeners for active attachment changes and database connection status.</description></item>
    /// <item><description>Processes watched attachments to queue uploads/downloads.</description></item>
    /// <item><description>Handles state transitions for archived and new attachments.</description></item>
    /// </list>
    /// Cancels any in-flight pipeline before launching a new one; safe to call repeatedly.
    /// </remarks>
    /// <returns>A task that completes when the sync loops have been started.</returns>
    public async Task StartSyncAsync()
    {
        await _startStopLock.WaitAsync();
        try
        {
            await StopSyncInternalAsync();

            await _options.LocalStorage.InitializeAsync();
            await VerifyAttachmentsAsync();

            _runCts = new CancellationTokenSource();
            var ct = _runCts.Token;

            await _syncingService.StartSyncAsync(_options.SyncInterval, ct);
            _statusLoop = StatusChangeLoopAsync(ct);
            _watchAttachmentsLoop = WatchAttachmentsLoopAsync(ct);
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    /// <summary>
    /// Requests a sync pass to run as soon as possible. Useful from error handlers ("retry now")
    /// or UI ("sync now"). Coalesces with any in-flight or pending sync; safe to call rapidly.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the trigger was buffered; <c>false</c> if sync isn't running or a trigger
    /// is already buffered.
    /// </returns>
    public bool TriggerSync() => _syncingService.TriggerSync();

    /// <summary>
    /// Synchronizes all active attachments between local and remote storage. Uploads pending files,
    /// downloads newly referenced ones, applies pending deletes, and prunes archived rows.
    /// </summary>
    /// <remarks>
    /// This is called automatically at regular intervals when sync is started, but can also be
    /// called manually to await an immediate sync pass (e.g. before shutting down). For
    /// "fire and forget", use <see cref="TriggerSync"/>.
    /// </remarks>
    /// <returns>A task that completes when the sync pass has finished.</returns>
    public Task SyncStorageAsync() => _syncingService.RunSyncPassAsync();

    /// <summary>
    /// Stops the attachment synchronization process. Cancels the sync pipeline, stops the periodic
    /// timer, and tears down all attachment watchers. Waits for any in-flight upload/download/delete
    /// to finish. Syncing can be resumed by calling <see cref="StartSyncAsync"/> again.
    /// </summary>
    /// <returns>A task that completes when all sync loops have unwound.</returns>
    public async Task StopSyncAsync()
    {
        await _startStopLock.WaitAsync();
        try
        {
            await StopSyncInternalAsync();
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    private async Task StopSyncInternalAsync()
    {
        _runCts?.Cancel();

        try
        {
            await Task.WhenAll(_syncingService.StopSyncAsync(), _statusLoop, _watchAttachmentsLoop);
        }
        catch (OperationCanceledException)
        {
            // Expected when CTS is cancelled.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while shutting down attachment sync loops");
        }

        _runCts?.Dispose();
        _runCts = null;
        _statusLoop = _watchAttachmentsLoop = Task.CompletedTask;
    }

    /// <summary>
    /// Saves a file to local storage and queues it for upload to remote storage.
    /// The file bytes are written to local storage immediately; the upload happens asynchronously
    /// on the next sync pass.
    /// </summary>
    /// <remarks>
    /// A <paramref name="updateHook"/> is provided which should be used when assigning relationships to the newly
    /// created attachment. This hook is executed in the same write transaction which creates the attachment record.
    /// </remarks>
    /// <param name="data">Stream of file bytes.</param>
    /// <param name="fileExtension">File extension (e.g. <c>"jpg"</c>) used to derive the filename.</param>
    /// <param name="mediaType">Optional MIME type.</param>
    /// <param name="metaData">Optional opaque metadata persisted with the attachment row.</param>
    /// <param name="id">Optional explicit attachment id; a UUID is generated when not supplied.</param>
    /// <param name="updateHook">
    /// Optional callback executed in the same write transaction as the attachment INSERT.
    /// Use this to atomically link the new attachment to your data model.
    /// </param>
    /// <returns>The created attachment record (with stamped <see cref="Attachment.Timestamp"/> and <see cref="Attachment.Size"/>).</returns>
    public async Task<Attachment> SaveFileAsync(
        Stream data,
        string fileExtension,
        string? mediaType = null,
        string? metaData = null,
        string? id = null,
        Func<ITransaction, Attachment, Task>? updateHook = null)
    {
        var resolvedId = id ?? await GenerateAttachmentIdAsync();
        var filename = $"{resolvedId}.{fileExtension}";
        var localUri = _options.LocalStorage.GetLocalUri(filename);
        var size = await _options.LocalStorage.SaveFileAsync(localUri, data);

        var attachment = new Attachment
        {
            Id = resolvedId,
            Filename = filename,
            State = AttachmentState.QueuedUpload,
            LocalUri = localUri,
            Size = size,
            MediaType = mediaType,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MetaData = metaData,
            HasSynced = false,
        };

        await _attachmentService.WithContextAsync(async ctx =>
            await ctx.Db.WriteTransaction(async tx =>
            {
                if (updateHook is not null)
                {
                    await updateHook(tx, attachment);
                }

                await ctx.UpsertAttachmentAsync(attachment, tx);
            }));

        return attachment;
    }

    /// <summary>
    /// Marks an existing attachment for deletion. The remote and local files are removed
    /// asynchronously on the next sync pass.
    /// </summary>
    /// <remarks>
    /// Use <paramref name="updateHook"/> to clear references from your data model in the same write
    /// transaction as the state transition, so the data model and the queue stay in sync.
    /// </remarks>
    /// <param name="id">The id of the attachment to delete.</param>
    /// <param name="updateHook">
    /// Optional callback executed in the same write transaction as the state transition.
    /// Use this to atomically clear references from your data model.
    /// </param>
    /// <returns>A task that completes once the attachment is queued for deletion.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no attachment with the given id exists.</exception>
    public Task DeleteFileAsync(
        string id,
        Func<ITransaction, Attachment, Task>? updateHook = null) => _attachmentService.WithContextAsync(async ctx =>
    {
        var attachment = await ctx.GetAttachmentAsync(id)
            ?? throw new InvalidOperationException($"Attachment with id {id} not found");

        await ctx.Db.WriteTransaction(async tx =>
        {
            if (updateHook is not null)
            {
                await updateHook(tx, attachment);
            }

            attachment.State = AttachmentState.QueuedDelete;
            attachment.HasSynced = false;
            await ctx.UpsertAttachmentAsync(attachment, tx);
        });
    });

    /// <summary>
    /// Removes archived attachments that exceed <see cref="AttachmentQueueOptions.ArchivedCacheLimit"/>,
    /// along with their local files. Archived rows up to the limit are kept as a cache so that briefly
    /// re-referenced attachments can be restored without a re-download.
    /// </summary>
    /// <returns>A task that completes when no archived rows remain past the cache limit.</returns>
    public async Task ExpireCacheAsync()
    {
        var done = false;
        while (!done)
        {
            await _attachmentService.WithContextAsync(async ctx =>
            {
                done = await _syncingService.DeleteArchivedAttachmentsAsync(ctx);
            });
        }
    }

    /// <summary>
    /// Clears the attachment queue and deletes all attachment files from local storage.
    /// </summary>
    /// <returns>A task that completes when both the queue table and the local storage have been cleared.</returns>
    public async Task ClearQueueAsync()
    {
        await _attachmentService.WithContextAsync(ctx => ctx.ClearQueueAsync());
        await _options.LocalStorage.ClearAsync();
    }

    /// <summary>
    /// Verifies the integrity of all attachment records and repairs inconsistencies. Checks each
    /// attachment record against the local filesystem and:
    /// <list type="bullet">
    /// <item><description>Updates <see cref="Attachment.LocalUri"/> if the file exists at a different path.</description></item>
    /// <item><description>Archives attachments with missing local files that haven't been uploaded.</description></item>
    /// <item><description>Requeues synced attachments for download if their local files are missing.</description></item>
    /// </list>
    /// </summary>
    /// <returns>A task that completes once the verification pass has saved any state corrections.</returns>
    public Task VerifyAttachmentsAsync() => _attachmentService.WithContextAsync(async ctx =>
    {
        var attachments = await ctx.GetAttachmentsAsync();
        var updates = new List<Attachment>();

        foreach (var attachment in attachments)
        {
            if (attachment.LocalUri is null)
            {
                continue;
            }

            if (await _options.LocalStorage.FileExistsAsync(attachment.LocalUri))
            {
                continue;
            }

            var newLocalUri = _options.LocalStorage.GetLocalUri(attachment.Filename);
            if (await _options.LocalStorage.FileExistsAsync(newLocalUri))
            {
                attachment.LocalUri = newLocalUri;
                updates.Add(attachment);
                continue;
            }

            attachment.LocalUri = null;
            attachment.State = attachment.State == AttachmentState.Synced
                ? AttachmentState.QueuedDownload
                : AttachmentState.Archived;
            updates.Add(attachment);
        }

        await ctx.SaveAttachmentsAsync(updates);
    });

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopSyncAsync();
    }

    private async Task StatusChangeLoopAsync(CancellationToken ct)
    {
        try
        {
            var previousConnected = _options.Db.CurrentStatus.Connected;
            await foreach (var @event in _options.Db.Events.OnStatusChanged.ListenAsync(ct))
            {
                if (!previousConnected && @event.Status.Connected)
                {
                    _syncingService.TriggerSync();
                }

                previousConnected = @event.Status.Connected;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
    }

    private async Task WatchAttachmentsLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var watched in _options.WatchAttachments(ct).WithCancellation(ct))
            {
                try
                {
                    await ProcessWatchedAttachmentsAsync(watched);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reconciling watched attachments");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
    }

    /// <summary>
    /// Given an array of attachment ids your data model says exist (watched), update the queue table so it matches: 
    /// add anything new, restore anything brought back, and archive anything no longer referenced.
    /// </summary>
    /// <param name="watched">An array of attachment ids to be monitored and updated.</param>
    /// <returns>A task that processes the watched attachments.</returns>
    private Task ProcessWatchedAttachmentsAsync(WatchedAttachmentItem[] watched) =>
        _attachmentService.WithContextAsync(async ctx =>
        {
            // Get all the attachments which are tracked in the DB.
            var current = await ctx.GetAttachmentsAsync();
            var updates = new List<Attachment>();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var item in watched)
            {
                var existing = current.FirstOrDefault(a => a.Id == item.Id);

                if (existing is null)
                {
                    if (!_options.DownloadAttachments)
                    {
                        continue;
                    }

                    // This item should be added to the queue.
                    // This item is assumed to be coming from an upstream sync.
                    // Locally created new items should be persisted using SaveFileAsync before this point.
                    var filename = item.Filename ?? $"{item.Id}.{item.FileExtension}";

                    updates.Add(new Attachment
                    {
                        Id = item.Id,
                        Filename = filename,
                        State = AttachmentState.QueuedDownload,
                        Timestamp = now,
                        MetaData = item.MetaData,
                        HasSynced = false,
                    });
                    continue;
                }

                if (existing.State == AttachmentState.Archived)
                {
                    // The attachment is present again. Need to queue it for sync.
                    if (existing.HasSynced)
                    {
                        // No remote action required, we can restore the record (avoids deletion).
                        existing.State = AttachmentState.Synced;
                    }
                    else
                    {
                        // LocalUri should be set if the record was meant to be downloaded and has been synced.
                        // If it's missing and HasSynced is false, then it must be an upload operation.
                        existing.State = existing.LocalUri is null
                            ? AttachmentState.QueuedDownload
                            : AttachmentState.QueuedUpload;
                    }

                    updates.Add(existing);
                }
            }

            // Archive any items not specified in the watched items.
            // For QueuedDelete or QueuedUpload states, archive only if HasSynced is true.
            // For other states, archive if the record is not found in the items.
            foreach (var attachment in current)
            {
                var stillReferenced = watched.Any(i => i.Id == attachment.Id);
                if (stillReferenced)
                {
                    // The record is in the watched items, no need to archive it.
                    continue;
                }

                switch (attachment.State)
                {
                    case AttachmentState.QueuedDelete:
                    case AttachmentState.QueuedUpload:
                        // Archive these records only if they have synced — otherwise we'd lose the user's pending write.
                        if (attachment.HasSynced)
                        {
                            attachment.State = AttachmentState.Archived;
                            updates.Add(attachment);
                        }
                        break;
                    default:
                        // Other states (e.g. QueuedDownload) can be archived since they're not in the watched items.
                        attachment.State = AttachmentState.Archived;
                        updates.Add(attachment);
                        break;
                }
            }

            await ctx.SaveAttachmentsAsync(updates);
        });
}
