using System.Runtime.CompilerServices;

using MAUITodo.Attachments;
using MAUITodo.Models;

using Microsoft.Extensions.Logging;

using PowerSync.Common.Attachments;
using PowerSync.Common.Client;
using PowerSync.Common.Client.Sync;
using PowerSync.Common.Client.Sync.Stream;
using PowerSync.Common.MDSQLite;
using PowerSync.Maui.SQLite;

namespace MAUITodo.Data;

public class PowerSyncData
{
    public PowerSyncDatabase Db;
    public AttachmentQueue PhotoAttachmentQueue { get; }
    private string UserId { get; }

    public PowerSyncData()
    {
        Console.WriteLine("Creating PowerSyncData instance");
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Error);
        });
        var logger = loggerFactory.CreateLogger("PowerSyncLogger");

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "example.db");
        var factory = new MAUISQLiteDBOpenFactory(new MDSQLiteOpenFactoryOptions()
        {
            DbFilename = dbPath
        });
        Db = new PowerSyncDatabase(new PowerSyncDatabaseOptions()
        {
            Database = factory,
            Schema = AppSchema.PowerSyncSchema,
            Logger = logger
        });

        var nodeConnector = new NodeConnector();
        UserId = nodeConnector.UserId;

        // Checkpoint requests let the app ask the service for a checkpoint on demand, which is what
        // the pull-to-refresh gestures use. Requires PowerSync service 1.24.0 or later.
        Db.Connect(nodeConnector, new PowerSyncConnectionOptions(
            checkpointMode: new CheckpointMode.Requests()));

        var attachmentsDir = Path.Combine(FileSystem.AppDataDirectory, "attachments");
        var localStorage = new FileManagerLocalStorage(attachmentsDir);
        var remoteStorage = new NodeRemoteStorageAdapter(new HttpClient(), nodeConnector.BackendUrl);

        PhotoAttachmentQueue = new AttachmentQueue(new AttachmentQueueOptions
        {
            Db = Db,
            LocalStorage = localStorage,
            RemoteStorage = remoteStorage,
            WatchAttachments = ct => WatchTodoPhotos(Db, ct),
        });

        _ = Task.Run(() => PhotoAttachmentQueue.StartSyncAsync());
    }

    private static async IAsyncEnumerable<WatchedAttachmentItem[]> WatchTodoPhotos(
        PowerSyncDatabase db,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var stream = db.Watch<PhotoIdResult>(
            "SELECT photo_id FROM todos WHERE photo_id IS NOT NULL",
            [],
            new SQLWatchOptions { TriggerImmediately = true, Signal = ct });
        await foreach (var rows in stream.WithCancellation(ct))
        {
            yield return [.. rows.Select(r => new WatchedAttachmentItem(r.photo_id, fileExtension: "jpg"))];
        }
    }

    private record PhotoIdResult(string photo_id);

    /// <summary>
    /// Asks the service for a checkpoint and waits until the local database has applied everything
    /// up to it, so the caller knows the local view has caught up.
    /// </summary>
    /// <exception cref="CheckpointRequestException">
    /// Thrown when the client is disconnected, was connected without checkpoint requests enabled,
    /// or a sync error occurs before the checkpoint is applied.
    /// </exception>
    public async Task RefreshAsync()
    {
        var checkpoint = await Db.RequestCheckpoint();
        await checkpoint.WaitForSync();
    }

    public async Task SaveListAsync(TodoList list)
    {
        if (list.ID != "")
        {
            await Db.Execute(
                "UPDATE lists SET name = ?, owner_id = ? WHERE id = ?",
                [list.Name, UserId, list.ID]);
        }
        else
        {
            await Db.Execute(
                "INSERT INTO lists (id, created_at, name, owner_id) VALUES (uuid(), datetime(), ?, ?)",
                [list.Name, UserId]);
        }
    }

    public async Task DeleteListAsync(TodoList list)
    {
        await Db.WriteTransaction(async tx =>
        {
            // Prevent attachments from being orphaned when their owning todo is deleted;
            // `has_synced = 0` prevents the attachments from becoming archived instead of deleted.
            //
            // It would be nice to be able to do this sort of bulk-delete using the attachments API directly.
            await tx.Execute(
                @"UPDATE attachments
                  SET state = ?, has_synced = 0
                  WHERE id IN (SELECT photo_id FROM todos WHERE list_id = ? AND photo_id IS NOT NULL)",
                [(int)AttachmentState.QueuedDelete, list.ID]);
            await tx.Execute("DELETE FROM todos WHERE list_id = ?", [list.ID]);
            await tx.Execute("DELETE FROM lists WHERE id = ?", [list.ID]);
        });

        // Force the attachments queue to acknowledge the changes immediately
        PhotoAttachmentQueue.TriggerSync();
    }

    public async Task SaveItemAsync(TodoItem item)
    {
        if (item.ID != "")
        {
            await Db.Execute(
                @"UPDATE todos
                  SET description = ?, completed = ?, completed_at = ?, completed_by = ?
                  WHERE id = ?",
                [
                    item.Description,
                    item.Completed,
                    item.CompletedAt,
                    item.Completed ? UserId : null,
                    item.ID
                ]);
        }
        else
        {
            await Db.Execute(
                @"INSERT INTO todos
                  (id, list_id, description, created_at, created_by, completed, completed_at, completed_by)
                  VALUES (uuid(), ?, ?, datetime(), ?, ?, ?, ?)",
                [
                    item.ListId,
                    item.Description,
                    UserId,
                    item.Completed ? 1 : 0,
                    item.CompletedAt!,
                    item.Completed ? UserId : null
                ]);
        }
    }

    public async Task SaveTodoPhotoAsync(string todoId, Stream photoData)
    {
        await PhotoAttachmentQueue.SaveFileAsync(
            data: photoData,
            fileExtension: "jpg",
            mediaType: "image/jpeg",
            updateHook: (tx, attachment) =>
                tx.Execute("UPDATE todos SET photo_id = ? WHERE id = ?", [attachment.Id, todoId]));
    }

    public async Task RemoveTodoPhotoAsync(string todoId, string photoId)
    {
        await PhotoAttachmentQueue.DeleteFileAsync(
            id: photoId,
            updateHook: (tx, _) =>
                tx.Execute("UPDATE todos SET photo_id = NULL WHERE id = ?", [todoId]));
    }

    public async Task SaveTodoCompletedAsync(string todoId, bool completed)
    {
        if (completed)
        {
            await Db.Execute(
                @"UPDATE todos
                  SET completed = 1, completed_at = datetime(), completed_by = ?
                  WHERE id = ?",
                [
                    UserId,
                    todoId
                ]);
        }
        else
        {
            await Db.Execute(
                @"UPDATE todos
                  SET completed = 0, completed_at = NULL, completed_by = NULL
                  WHERE id = ?",
                [
                    todoId
                ]);
        }
    }

    public async Task DeleteItemAsync(TodoItem item)
    {
        if (item.PhotoId != null)
        {
            // DeleteFileAsync can fail if a `todos` row is synced with a photo_id, but the 
            // corresponding `attachments` row/item hasn't been created yet. Fallback to regular
            // `DELETE FROM todos`.
            try
            {
                await PhotoAttachmentQueue.DeleteFileAsync(
                    id: item.PhotoId,
                    updateHook: (tx, _) =>
                        tx.Execute("DELETE FROM todos WHERE id = ?", [item.ID]));
            }
            catch
            {
                await Db.Execute("DELETE FROM todos WHERE id = ?", [item.ID]);
            }
        }
        else
        {
            await Db.Execute("DELETE FROM todos WHERE id = ?", [item.ID]);
        }
    }
}
