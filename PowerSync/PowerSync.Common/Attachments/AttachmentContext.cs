namespace PowerSync.Common.Attachments;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using PowerSync.Common.Client;
using PowerSync.Common.DB;

/// <summary>
/// Database operations for managing attachment records.
/// Provides query, insert, update and delete primitives with transaction-aware overloads.
/// </summary>
internal sealed class AttachmentContext(IPowerSyncDatabase db, string tableName, int maxArchivedCount, ILogger logger)
{
    /// <summary>The PowerSync database used for queries.</summary>
    public IPowerSyncDatabase Db => db;

    public Task DeleteAttachmentAsync(string id) => db.WriteTransaction(tx => tx.Execute(
        $"DELETE FROM {tableName} WHERE id = ?",
        [id]));

    public Task IgnoreAttachmentAsync(string id) => db.Execute(
        $"UPDATE {tableName} SET state = ? WHERE id = ?",
        [(int)AttachmentState.Archived, id]);

    public Task<Attachment?> GetAttachmentAsync(string id) => db.GetOptional<Attachment>(
        $"SELECT * FROM {tableName} WHERE id = ?",
        [id]);

    public Task<Attachment> SaveAttachmentAsync(Attachment attachment) => db.WriteLock(async ctx =>
    {
        await UpsertAttachmentAsync(attachment, ctx);
        return attachment;
    });

    public Task SaveAttachmentsAsync(IReadOnlyList<Attachment> attachments)
    {
        if (attachments.Count == 0)
        {
            logger.LogDebug("No attachments to save.");
            return Task.CompletedTask;
        }

        return db.WriteTransaction(async tx =>
        {
            foreach (var attachment in attachments)
            {
                await UpsertAttachmentAsync(attachment, tx);
            }
        });
    }

    public Task<string[]> GetAttachmentIdsAsync() => db.GetAll<string>(
        $"SELECT id FROM {tableName} WHERE id IS NOT NULL");

    public Task<Attachment[]> GetAttachmentsAsync() => db.GetAll<Attachment>(
        $@"
            SELECT *
            FROM {tableName}
            ORDER BY timestamp ASC
        ");

    public Task<Attachment[]> GetActiveAttachmentsAsync() => db.GetAll<Attachment>(
        $@"
            SELECT *
            FROM {tableName}
            WHERE state != ?
            ORDER BY timestamp ASC
        ",
        [(int)AttachmentState.Archived]);

    public Task ClearQueueAsync() => db.WriteTransaction(tx => tx.Execute(
        $"DELETE FROM {tableName}"));

    public async Task<bool> DeleteArchivedAttachmentsAsync(Func<Attachment[], Task>? callback = null, int limit = 1000)
    {
        var archived = await db.GetAll<Attachment>(
            $@"
                SELECT *
                FROM {tableName}
                WHERE state = ?
                ORDER BY timestamp DESC
                LIMIT ?
                OFFSET ?
            ",
            [(int)AttachmentState.Archived, limit, maxArchivedCount]);

        if (archived.Length == 0)
        {
            return true;
        }

        logger.LogInformation(
            "Deleting {Count} archived attachments, (exceeding maxArchivedCount={MaxArchivedCount})...",
            archived.Length,
            maxArchivedCount);

        // Call the callback with the list of archived attachments before deletion.
        if (callback is not null)
        {
            await callback(archived);
        }

        // Delete the archived attachments from the table.
        var ids = archived.Select(a => a.Id).ToArray();
        await db.Execute(
            $"DELETE FROM {tableName} WHERE id IN (SELECT json_each.value FROM json_each(?))",
            [JsonConvert.SerializeObject(ids)]);

        logger.LogInformation("Deleted {Count} archived attachments", archived.Length);
        return archived.Length < limit;
    }

    public Task UpsertAttachmentAsync(Attachment attachment, ILockContext ctx)
    {
        logger.LogDebug("Updating attachment {Id}: {State}", attachment.Id, attachment.State);

        return ctx.Execute(
            $@"
                INSERT OR REPLACE INTO {tableName} (
                    id, timestamp, filename, local_uri, media_type, size, state, has_synced, meta_data
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            ",
            [
                attachment.Id,
                attachment.Timestamp,
                attachment.Filename,
                attachment.LocalUri,
                attachment.MediaType,
                attachment.Size,
                (int)attachment.State,
                attachment.HasSynced,
                attachment.MetaData,
            ]);
    }
}
