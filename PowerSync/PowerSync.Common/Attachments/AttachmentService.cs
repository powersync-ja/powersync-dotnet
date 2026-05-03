namespace PowerSync.Common.Attachments;

using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

using PowerSync.Common.Client;

/// <summary>
/// Service responsible for managing attachment synchronization state and operations, providing thread-safe
/// access to the underlying <see cref="AttachmentContext"/>.
/// </summary>
internal sealed class AttachmentService(PowerSyncDatabase db, string tableName, int archivedCacheLimit, ILogger logger)
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly AttachmentContext _context = new(db, tableName, archivedCacheLimit, logger);

    /// <summary>
    /// Reactive watch over attachments needing synchronization (Queued{Upload,Download,Delete}).
    /// </summary>
    /// <param name="ct">Cancellation token to stop watching.</param>
    public async IAsyncEnumerable<bool> WatchActiveAttachments(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var stream = db.Watch<string>(
            $@"
                SELECT id
                FROM {tableName}
                WHERE state = ? OR state = ? OR state = ?
                ORDER BY timestamp ASC
            ",
            [(int)AttachmentState.QueuedUpload, (int)AttachmentState.QueuedDownload, (int)AttachmentState.QueuedDelete],
            new SQLWatchOptions { Signal = ct, TriggerImmediately = true });

        await foreach (var _ in stream.WithCancellation(ct))
        {
            yield return true;
        }
    }

    /// <summary>
    /// Executes the <paramref name="callback"/> with exclusive access to the underlying <see cref="AttachmentContext"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value returned by the callback.</typeparam>
    /// <param name="callback">The callback to execute with the attachment context.</param>
    /// <returns>The task result is the value returned by the callback.</returns>
    public async Task<T> WithContextAsync<T>(Func<AttachmentContext, Task<T>> callback)
    {
        await _mutex.WaitAsync();
        try
        {
            return await callback(_context);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// Executes the <paramref name="callback"/> with exclusive access to the underlying <see cref="AttachmentContext"/>.
    /// </summary>
    /// <param name="callback">The callback to execute with the attachment context.</param>
    /// <returns>The void variant task.</returns>
    /// <remarks>Mutex-protected void variant of <see cref="WithContextAsync{T}"/>.</remarks>
    public async Task WithContextAsync(Func<AttachmentContext, Task> callback)
    {
        await _mutex.WaitAsync();
        try
        {
            await callback(_context);
        }
        finally
        {
            _mutex.Release();
        }
    }
}
