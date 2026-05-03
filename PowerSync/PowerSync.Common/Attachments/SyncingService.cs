namespace PowerSync.Common.Attachments;

using System.Diagnostics;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

/// <summary>
/// Service responsible for syncing attachments between local and remote storage.
/// </summary>
/// <remarks>
/// This service handles downloading, uploading, and deleting attachments, as well as
/// periodically syncing attachment states. It ensures proper lifecycle management
/// of sync operations and provides mechanisms for error handling and retries.
/// </remarks>
internal sealed class SyncingService(
    AttachmentService attachmentService,
    ILocalStorageAdapter localStorage,
    IRemoteStorageAdapter remoteStorage,
    IAttachmentErrorHandler? errorHandler,
    TimeSpan syncThrottle,
    ILogger logger)
{
    private readonly SemaphoreSlim _startStopLock = new(1, 1);
    private CancellationTokenSource? _internalCts;
    private Channel<bool>? _syncSignals;

    private Task _consumerLoop = Task.CompletedTask;
    private Task _watchProducerLoop = Task.CompletedTask;
    private Task _periodicProducerLoop = Task.CompletedTask;

    /// <summary>
    /// Starts the syncing process, including periodic and event-driven sync operations.
    /// Cancels any in-flight pipeline before launching a new one.
    /// </summary>
    /// <param name="period">The interval at which periodic sync operations are triggered.</param>
    /// <param name="ct">Cancellation token; loops stop when cancelled.</param>
    /// <returns>A task that completes once the loops have been started.</returns>
    public async Task StartSyncAsync(TimeSpan period, CancellationToken ct)
    {
        await _startStopLock.WaitAsync(ct);
        try
        {
            await StopSyncInternalAsync();

            _internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _internalCts.Token;

            _syncSignals = Channel.CreateBounded<bool>(
                new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

            _consumerLoop = SyncSignalConsumerAsync(token);
            _watchProducerLoop = WatchSignalProducerAsync(token);
            _periodicProducerLoop = PeriodicSignalProducerAsync(period, token);
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    /// <summary>
    /// Manually enqueues a sync pass.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the trigger was buffered; <c>false</c> if the service isn't running or
    /// a trigger is already buffered.
    /// </returns>
    public bool TriggerSync() => _syncSignals?.Writer.TryWrite(true) ?? false;

    /// <summary>
    /// Stops the sync pipeline and waits for its loops to drain.
    /// Safe to call when not running; safe to call concurrently with <see cref="StartSyncAsync"/>.
    /// </summary>
    /// <returns>A task that completes when the consumer and producer loops have all returned.</returns>
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
        _internalCts?.Cancel();
        _syncSignals?.Writer.TryComplete();

        try
        {
            await Task.WhenAll(_consumerLoop, _watchProducerLoop, _periodicProducerLoop);
        }
        catch (OperationCanceledException)
        {
            // Expected when CTS is cancelled.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error while shutting down syncing service");
        }

        _internalCts?.Dispose();
        _internalCts = null;
        _syncSignals = null;
        _consumerLoop = _watchProducerLoop = _periodicProducerLoop = Task.CompletedTask;
    }

    /// <summary>
    /// Runs one sync pass: fetches active attachments, processes them, then prunes archived rows.
    /// </summary>
    /// <returns>A task that completes when the pass has finished.</returns>
    public Task RunSyncPassAsync() => attachmentService.WithContextAsync(async ctx =>
    {
        var active = await ctx.GetActiveAttachmentsAsync();
        await ProcessAttachmentsAsync(active, ctx);
        await DeleteArchivedAttachmentsAsync(ctx);
    });

    /// <summary>
    /// Processes attachments based on their state. Updates are saved in a single batch.
    /// </summary>
    /// <param name="attachments">Attachment records to process.</param>
    /// <param name="context">Attachment context for database operations.</param>
    /// <returns>A task that completes once all attachments have been processed and saved.</returns>
    public async Task ProcessAttachmentsAsync(IReadOnlyList<Attachment> attachments, AttachmentContext context)
    {
        var updates = new List<Attachment>();

        foreach (var attachment in attachments)
        {
            Attachment? changed = attachment.State switch
            {
                AttachmentState.QueuedUpload => await UploadAttachmentAsync(attachment),
                AttachmentState.QueuedDownload => await DownloadAttachmentAsync(attachment),
                AttachmentState.QueuedDelete => await DeleteAttachmentAsync(attachment, context),
                _ => null,
            };

            if (changed is not null)
            {
                updates.Add(changed);
            }
        }

        await context.SaveAttachmentsAsync(updates);
    }

    /// <summary>
    /// Uploads an attachment from local storage to remote storage.
    /// On success, marks <see cref="AttachmentState.Synced"/> with <c>HasSynced = true</c>.
    /// On failure, defers to <see cref="IAttachmentErrorHandler"/> or archives.
    /// </summary>
    /// <param name="attachment">The attachment to upload.</param>
    /// <returns>The updated attachment, or <c>null</c> if no DB write is needed.</returns>
    public async Task<Attachment?> UploadAttachmentAsync(Attachment attachment)
    {
        logger.LogInformation("Uploading attachment {Filename}", attachment.Filename);
        try
        {
            if (attachment.LocalUri is null)
            {
                throw new InvalidOperationException($"No LocalUri for attachment {attachment.Id}");
            }

            using (var stream = await localStorage.ReadFileAsync(attachment.LocalUri))
            {
                await remoteStorage.UploadFileAsync(stream, attachment);
            }

            attachment.State = AttachmentState.Synced;
            attachment.HasSynced = true;
            return attachment;
        }
        catch (Exception error)
        {
            var shouldRetry = errorHandler is null || await errorHandler.OnUploadErrorAsync(attachment, error);

            if (shouldRetry)
            {
                return null;
            }

            attachment.State = AttachmentState.Archived;
            return attachment;
        }
    }

    /// <summary>
    /// Downloads an attachment from remote storage to local storage.
    /// On success, marks <see cref="AttachmentState.Synced"/> with <c>LocalUri</c> populated.
    /// On failure, defers to <see cref="IAttachmentErrorHandler"/> or archives.
    /// </summary>
    /// <param name="attachment">The attachment to download.</param>
    /// <returns>The updated attachment, or <c>null</c> if no DB write is needed.</returns>
    public async Task<Attachment?> DownloadAttachmentAsync(Attachment attachment)
    {
        logger.LogInformation("Downloading attachment {Filename}", attachment.Filename);
        try
        {
            var localUri = localStorage.GetLocalUri(attachment.Filename);
            using (var stream = await remoteStorage.DownloadFileAsync(attachment))
            {
                await localStorage.SaveFileAsync(localUri, stream);
            }

            attachment.State = AttachmentState.Synced;
            attachment.LocalUri = localUri;
            attachment.HasSynced = true;
            return attachment;
        }
        catch (Exception error)
        {
            var shouldRetry = errorHandler is null || await errorHandler.OnDownloadErrorAsync(attachment, error);

            if (shouldRetry)
            {
                return null;
            }

            attachment.State = AttachmentState.Archived;
            return attachment;
        }
    }

    /// <summary>
    /// Deletes an attachment from both remote and local storage and removes the record.
    /// On failure, defers to <see cref="IAttachmentErrorHandler"/> or archives.
    /// </summary>
    /// <param name="attachment">The attachment to delete.</param>
    /// <param name="context">The attachment context for database operations.</param>
    /// <returns>The updated attachment, or <c>null</c> on successful delete or transient failure to retry.</returns>
    public async Task<Attachment?> DeleteAttachmentAsync(Attachment attachment, AttachmentContext context)
    {
        try
        {
            await remoteStorage.DeleteFileAsync(attachment);
            if (attachment.LocalUri is not null)
            {
                await localStorage.DeleteFileAsync(attachment.LocalUri);
            }

            await context.DeleteAttachmentAsync(attachment.Id);
            return null;
        }
        catch (Exception error)
        {
            var shouldRetry = errorHandler is null || await errorHandler.OnDeleteErrorAsync(attachment, error);

            if (shouldRetry)
            {
                return null;
            }

            attachment.State = AttachmentState.Archived;
            return attachment;
        }
    }

    /// <summary>
    /// Cleans up archived attachments by removing their local files and records.
    /// Errors during local file deletion are logged but do not prevent record deletion.
    /// </summary>
    /// <param name="context">The attachment context for database operations.</param>
    /// <param name="limit">Maximum number of archived attachments to delete in this batch.</param>
    /// <returns><c>true</c> when no further work remains, <c>false</c> when more rows are eligible.</returns>
    public Task<bool> DeleteArchivedAttachmentsAsync(AttachmentContext context, int limit = 1000) =>
        context.DeleteArchivedAttachmentsAsync(async archived =>
        {
            foreach (var attachment in archived)
            {
                if (attachment.LocalUri is not null)
                {
                    try
                    {
                        await localStorage.DeleteFileAsync(attachment.LocalUri);
                    }
                    catch (Exception error)
                    {
                        logger.LogError(error, "Error deleting local file for archived attachment");
                    }
                }
            }
        }, limit);

    /// <summary>
    /// Consumer loop. Drains <c>_syncSignals</c> and runs <see cref="RunSyncPassAsync"/> per signal,
    /// applying <c>syncThrottle</c> between consecutive pass starts.
    /// </summary>
    /// <param name="ct">Cancellation token; loop exits when cancelled.</param>
    /// <returns>A task that completes when the loop returns.</returns>
    private async Task SyncSignalConsumerAsync(CancellationToken ct)
    {
        var reader = _syncSignals!.Reader;

        // Gate against the start of the previous pass.
        long? previousStart = null;

        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (reader.TryRead(out _))
                {
                    if (previousStart.HasValue && syncThrottle > TimeSpan.Zero)
                    {
                        var elapsed = (double)(Stopwatch.GetTimestamp() - previousStart) / Stopwatch.Frequency;
                        var remainder = syncThrottle - TimeSpan.FromSeconds(elapsed);
                        if (remainder > TimeSpan.Zero)
                        {
                            try
                            {
                                await Task.Delay(remainder, ct);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                        }
                    }

                    previousStart = Stopwatch.GetTimestamp();

                    try
                    {
                        await RunSyncPassAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error during attachment sync");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
    }

    /// <summary>
    /// Producer loop. Subscribes to <see cref="AttachmentService.WatchActiveAttachments"/> and
    /// writes a signal to <c>_syncSignals</c> for each emission.
    /// </summary>
    /// <param name="ct">Cancellation token; loop exits when cancelled.</param>
    /// <returns>A task that completes when the loop returns.</returns>
    private async Task WatchSignalProducerAsync(CancellationToken ct)
    {
        var writer = _syncSignals!.Writer;
        try
        {
            await foreach (var _ in attachmentService.WatchActiveAttachments(ct))
            {
                writer.TryWrite(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
    }

    /// <summary>
    /// Producer loop. Writes a signal to <c>_syncSignals</c> on a fixed interval so a sync pass
    /// runs even when no watch / manual trigger has fired (retry safety net for failed transfers).
    /// </summary>
    /// <param name="period">Interval between periodic signal emissions.</param>
    /// <param name="ct">Cancellation token; loop exits when cancelled.</param>
    /// <returns>A task that completes when the loop returns.</returns>
    private async Task PeriodicSignalProducerAsync(TimeSpan period, CancellationToken ct)
    {
        var writer = _syncSignals!.Writer;
        try
        {
#if NET6_0_OR_GREATER
            using var timer = new PeriodicTimer(period);
            while (await timer.WaitForNextTickAsync(ct))
            {
                writer.TryWrite(true);
            }
#else
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(period, ct);
                writer.TryWrite(true);
            }
#endif
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
    }
}
