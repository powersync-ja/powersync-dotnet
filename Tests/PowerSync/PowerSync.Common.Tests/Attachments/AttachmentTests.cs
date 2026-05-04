namespace PowerSync.Common.Tests.Attachments;

using System.Runtime.CompilerServices;

using PowerSync.Common.Attachments;
using PowerSync.Common.Client;
using PowerSync.Common.DB.Schema;
using PowerSync.Common.Tests.Utils;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "AttachmentTests"
/// </summary>
[Collection("AttachmentTests")]
public class AttachmentTests : IAsyncLifetime
{
    private PowerSyncDatabase _db = default!;
    private string _dbName = default!;
    private string _attachmentsDir = default!;
    private FileManagerLocalStorage _localStorage = default!;

    public async Task InitializeAsync()
    {
        _dbName = $"attachments-{Guid.NewGuid():N}.db";
        _attachmentsDir = Path.Combine(Path.GetTempPath(), $"attachments-{Guid.NewGuid():N}");
        _localStorage = new FileManagerLocalStorage(_attachmentsDir);

        var users = new Table
        {
            Name = "users",
            Columns =
            {
                ["name"] = ColumnType.Text,
                ["email"] = ColumnType.Text,
                ["photo_id"] = ColumnType.Text,
            },
        };

        _db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = _dbName },
            Schema = new Schema(users, new Table(typeof(Attachment))),
        });
        await _db.Init();
    }

    public async Task DisposeAsync()
    {
        await _db.DisconnectAndClear();
        await _db.Close();
        DatabaseUtils.CleanDb(_dbName);

        if (Directory.Exists(_attachmentsDir))
        {
            try { Directory.Delete(_attachmentsDir, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
    }

    private sealed class UserPhotoRow
    {
        public string photo_id { get; set; } = string.Empty;
    }

    private sealed class AttachmentResult
    {
        public string id { get; set; } = string.Empty;
        public long state { get; set; }
        public string? local_uri { get; set; }
        public string filename { get; set; } = string.Empty;
        public bool has_synced { get; set; }
    }

    [Fact(Timeout = 10000)]
    public async Task AttachmentDownload()
    {
        var mock = new MockRemoteStorage { DownloadResult = [1, 2, 3] };

        await using var queue = new AttachmentQueue(new AttachmentQueueOptions
        {
            Db = _db,
            LocalStorage = _localStorage,
            RemoteStorage = mock,
            WatchAttachments = ct => WatchUserPhotosAsync(_db, ct),
        });

        await queue.StartSyncAsync();

        // Create a user which has a photo_id associated.
        // This will be treated as a download since no attachment record was created.
        await _db.Execute(
            "INSERT INTO users (id, name, email, photo_id) VALUES (uuid(), 'user', 'user@example.com', uuid())");

        var attachment = await TestUtils.WaitForMatchAsync(
            ct => _db.Watch<AttachmentResult>(
                "SELECT id, state, local_uri, filename, has_synced FROM attachments",
                [],
                new SQLWatchOptions { TriggerImmediately = true, Signal = ct }),
            r => r.state == (long)AttachmentState.Synced,
            TimeSpan.FromSeconds(5));

        Assert.NotNull(attachment.local_uri);

        // The file should exist.
        await using var stream = await _localStorage.ReadFileAsync(attachment.local_uri!);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal(3, ms.Length);
    }

    [Fact(Timeout = 10000)]
    public async Task AttachmentUpload()
    {
        var mock = new MockRemoteStorage();

        await using var queue = new AttachmentQueue(new AttachmentQueueOptions
        {
            Db = _db,
            LocalStorage = _localStorage,
            RemoteStorage = mock,
            WatchAttachments = ct => WatchUserPhotosAsync(_db, ct),
        });

        await queue.StartSyncAsync();
        await SaveAndAwaitSyncedAsync(queue);

        Assert.True(mock.UploadCalled);
    }

    [Fact(Timeout = 10000)]
    public async Task AttachmentDelete()
    {
        var mock = new MockRemoteStorage();

        await using var queue = new AttachmentQueue(new AttachmentQueueOptions
        {
            Db = _db,
            LocalStorage = _localStorage,
            RemoteStorage = mock,
            WatchAttachments = ct => WatchUserPhotosAsync(_db, ct),
        });

        await queue.StartSyncAsync();
        var saved = await SaveAndAwaitSyncedAsync(queue);

        await queue.StopSyncAsync();
        await _db.Execute(
            "UPDATE attachments SET state = ? WHERE id = ?",
            [(int)AttachmentState.QueuedDelete, saved.Id]);
        await queue.SyncStorageAsync();

        var rows = await _db.GetAll<AttachmentResult>(
            "SELECT id, state, local_uri, filename, has_synced FROM attachments WHERE id = ?",
            [saved.Id]);
        Assert.Empty(rows);
        Assert.True(mock.DeleteCalled);
    }

    private async Task<Attachment> SaveAndAwaitSyncedAsync(AttachmentQueue queue)
    {
        using var data = new MemoryStream([3, 4, 5]);
        var saved = await queue.SaveFileAsync(
            data: data,
            fileExtension: "jpg",
            mediaType: "image/jpg",
            updateHook: (tx, attachment) => tx.Execute(
                "INSERT INTO users (id, name, email, photo_id) VALUES (uuid(), 'john', 'j@j.com', ?)",
                [attachment.Id]));

        await TestUtils.WaitForMatchAsync(
            ct => _db.Watch<AttachmentResult>(
                "SELECT id, state, local_uri, filename, has_synced FROM attachments",
                [],
                new SQLWatchOptions { TriggerImmediately = true, Signal = ct }),
            r => r.state == (long)AttachmentState.Synced,
            TimeSpan.FromSeconds(5));

        return saved;
    }

    [Fact(Timeout = 10000)]
    public async Task AttachmentInitVerification()
    {
        Directory.CreateDirectory(_attachmentsDir);

        const string filename = "test.jpeg";
        var realPath = Path.Combine(_attachmentsDir, filename);
        File.WriteAllBytes(realPath, [(byte)'1']);

        var attachmentId = Guid.NewGuid().ToString("N");
        var brokenPath = Path.Combine(Path.GetTempPath(), "not_attachments", filename);
        await _db.Execute(
            @"
                INSERT OR REPLACE INTO attachments
                    (id, timestamp, filename, local_uri, media_type, size, state, has_synced, meta_data)
                VALUES
                    (?, ?, ?, ?, ?, ?, ?, ?, ?)
            ",
            [
                attachmentId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                filename,
                brokenPath,
                "application/jpeg",
                1L,
                (int)AttachmentState.Synced,
                1L,
                string.Empty,
            ]);

        await _db.Execute(
            "INSERT INTO users (id, name, email, photo_id) VALUES (uuid(), 'user', 'user@example.com', ?)",
            [attachmentId]);

        await using var queue = new AttachmentQueue(new AttachmentQueueOptions
        {
            Db = _db,
            LocalStorage = _localStorage,
            RemoteStorage = new MockRemoteStorage { DownloadResult = [1, 2, 3] },
            WatchAttachments = ct => WatchUserPhotosAsync(_db, ct),
        });

        await queue.StartSyncAsync();
        await queue.StopSyncAsync();

        var attachments = await _db.GetAll<AttachmentResult>(
            "SELECT id, state, local_uri, filename, has_synced FROM attachments",
            []);

        Assert.Single(attachments);
        Assert.Equal(realPath, attachments[0].local_uri);
        Assert.Equal((long)AttachmentState.Synced, attachments[0].state);
    }

    private static async IAsyncEnumerable<WatchedAttachmentItem[]> WatchUserPhotosAsync(
        PowerSyncDatabase db,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var stream = db.Watch<UserPhotoRow>(
            "SELECT photo_id FROM users WHERE photo_id IS NOT NULL",
            [],
            new SQLWatchOptions { TriggerImmediately = true, Signal = ct });
        await foreach (var rows in stream.WithCancellation(ct))
        {
            yield return [.. rows.Select(r => new WatchedAttachmentItem(r.photo_id, fileExtension: "jpg"))];
        }
    }

    private sealed class MockRemoteStorage : IRemoteStorageAdapter
    {
        public byte[] DownloadResult { get; init; } = [];
        public bool UploadCalled { get; private set; }
        public bool DeleteCalled { get; private set; }

        /// <summary>
        /// Uploads a file to remote storage.
        /// </summary>
        public Task UploadFileAsync(Stream fileData, Attachment attachment)
        {
            UploadCalled = true;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Downloads a file from remote storage.
        /// </summary>
        public Task<Stream> DownloadFileAsync(Attachment attachment) =>
            Task.FromResult<Stream>(new MemoryStream(DownloadResult));

        /// <summary>
        /// Deletes a file from remote storage.
        /// </summary>
        public Task DeleteFileAsync(Attachment attachment)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }
    }
}
