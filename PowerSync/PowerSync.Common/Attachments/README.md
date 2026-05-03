# Attachments utilities and functions

> [!NOTE]
> Attachment helpers are currently in an **alpha** state, intended strictly for testing. Expect breaking changes and instability as development continues.
>
> Do not rely on this package for production use.

PowerSync utilities and classes managing file attachments in .NET applications. Automatically handles synchronization of files between local storage and remote storage (S3, Supabase Storage, Azure Blob, etc.), with support for upload/download queuing, offline functionality, and cache management.

For detailed concepts and guides, see the [PowerSync documentation](https://docs.powersync.com/usage/use-case-examples/attachments-files).

## Quick Start

This example shows a .NET application where users have profile photos stored as attachments.

### 1. Add the Attachment table to your schema

```csharp
using PowerSync.Common.Attachments;
using PowerSync.Common.DB.Schema;

var users = new Table(
    "users",
    new Dictionary<string, ColumnType>
    {
        ["name"] = ColumnType.Text,
        ["email"] = ColumnType.Text,
        ["photo_id"] = ColumnType.Text,
    });

var schema = new Schema(users, new Table(typeof(Attachment)));
```

### 2. Set up storage adapters

```csharp
using PowerSync.Common.Attachments;

// Local storage backed by System.IO.File
var localStorage = new FileManagerLocalStorage(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "myapp", "attachments"));

// In MyRemoteStorage.cs - adapter for your cloud storage (e.g., S3, Supabase, Azure Blob)
public sealed class MyRemoteStorage(HttpClient http) : IRemoteStorageAdapter
{
    public async Task UploadFileAsync(Stream fileData, Attachment attachment)
    {
        // Get signed upload URL from your backend
        var resp = await http.PostAsJsonAsync("/api/attachments/upload-url",
            new { filename = attachment.Filename });
        var body = await resp.Content.ReadFromJsonAsync<UploadUrlResponse>()
            ?? throw new InvalidOperationException("missing body");

        // Upload file to cloud storage
        using var content = new StreamContent(fileData);
        content.Headers.ContentType = new(attachment.MediaType ?? "application/octet-stream");
        (await http.PutAsync(body.UploadUrl, content)).EnsureSuccessStatusCode();
    }

    public async Task<Stream> DownloadFileAsync(Attachment attachment)
    {
        // Get signed download URL from your backend
        var url = await http.GetStringAsync($"/api/attachments/download-url/{attachment.Id}");

        // Download file from cloud storage
        return await http.GetStreamAsync(url);
    }

    public async Task DeleteFileAsync(Attachment attachment)
    {
        // Delete from cloud storage via your backend; 404 is treated as success (already gone).
        var resp = await http.DeleteAsync($"/api/attachments/{attachment.Id}");
        if (resp.StatusCode != HttpStatusCode.NotFound)
        {
            resp.EnsureSuccessStatusCode();
        }
    }

    private sealed record UploadUrlResponse(string UploadUrl);
}
```

> **Note:** `FileManagerLocalStorage` ships as the default `ILocalStorageAdapter` and works on every .NET target (Windows, macOS, Linux, iOS, Android). Implement `ILocalStorageAdapter` directly if you need a different backing store.

### 3. Create and start AttachmentQueue

```csharp
using PowerSync.Common.Attachments;

var remoteStorage = new MyRemoteStorage(httpClient);

var queue = new AttachmentQueue(new AttachmentQueueOptions
{
    Db = powersync,
    LocalStorage = localStorage,
    RemoteStorage = remoteStorage,
    // Determine what attachments the queue should handle -
    // in this case it handles only the user profile pictures.
    WatchAttachments = ct => WatchProfilePhotos(powersync, ct),
});

// Start automatic syncing
await queue.StartSyncAsync();

static async IAsyncEnumerable<WatchedAttachmentItem[]> WatchProfilePhotos(
    PowerSyncDatabase db,
    [EnumeratorCancellation] CancellationToken ct)
{
    var stream = db.Watch<UserPhotoRow>(
        "SELECT photo_id FROM users WHERE photo_id IS NOT NULL",
        null,
        new SQLWatchOptions { TriggerImmediately = true, Signal = ct });

    await foreach (var rows in stream.WithCancellation(ct))
    {
        yield return [.. rows.Select(r => new WatchedAttachmentItem(r.photo_id, fileExtension: "jpg"))];
    }
}

internal sealed class UserPhotoRow { public string photo_id { get; set; } = ""; }
```

### 4. Save files with atomic updates

```csharp
// When user uploads a profile photo
async Task UploadProfilePhotoAsync(Stream imageStream, string currentUserId)
{
    var attachment = await queue.SaveFileAsync(
        data: imageStream,
        fileExtension: "jpg",
        mediaType: "image/jpeg",
        // Atomically update the user record in the same transaction
        updateHook: async (tx, attachment) =>
        {
            await tx.Execute(
                "UPDATE users SET photo_id = ? WHERE id = ?",
                [attachment.Id, currentUserId]);
        });

    Console.WriteLine($"Photo queued for upload: {attachment.Id}");
    // File will automatically upload in the background
}
```

## Storage Adapters

### Local Storage Adapters

Local storage adapters handle file persistence on the device.

#### FileManagerLocalStorage

Default adapter backed by `System.IO.File`. Works on every .NET target.

```csharp
using PowerSync.Common.Attachments;

var localStorage = new FileManagerLocalStorage("./attachments");
```

**Constructor Parameters:**

- `attachmentsDirectory` (string, required): Directory path under which attachment files are stored. The directory is created on `InitializeAsync()`.

#### Custom Local Storage Adapter

Implement the `ILocalStorageAdapter` interface for other environments (e.g. an in-memory adapter for tests):

```csharp
public interface ILocalStorageAdapter
{
    Task InitializeAsync();
    Task ClearAsync();
    string GetLocalUri(string filename);
    Task<long> SaveFileAsync(string filePath, Stream data);
    Task<Stream> ReadFileAsync(string filePath);
    Task DeleteFileAsync(string filePath);
    Task<bool> FileExistsAsync(string filePath);
    Task CreateDirectoryAsync(string path);
    Task RemoveDirectoryAsync(string path);
}
```

### Remote Storage Adapter

Remote storage adapters handle communication with your cloud storage (S3, Supabase Storage, Cloudflare R2, Azure Blob, etc.).

#### Interface

```csharp
public interface IRemoteStorageAdapter
{
    Task UploadFileAsync(Stream fileData, Attachment attachment);
    Task<Stream> DownloadFileAsync(Attachment attachment);
    Task DeleteFileAsync(Attachment attachment);
}
```

#### Example: S3-Compatible Storage with Signed URLs

```csharp
public sealed class S3RemoteStorage(HttpClient http, Func<string> getAuthToken) : IRemoteStorageAdapter
{
    public async Task UploadFileAsync(Stream fileData, Attachment attachment)
    {
        // Request signed upload URL from your backend
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/attachments/upload-url")
        {
            Content = JsonContent.Create(new
            {
                filename = attachment.Filename,
                contentType = attachment.MediaType,
            }),
        };
        req.Headers.Authorization = new("Bearer", getAuthToken());

        var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<UploadUrlResponse>()
            ?? throw new InvalidOperationException("missing body");

        // Upload directly to S3 using signed URL
        using var content = new StreamContent(fileData);
        content.Headers.ContentType = new(attachment.MediaType ?? "application/octet-stream");
        (await http.PutAsync(body.UploadUrl, content)).EnsureSuccessStatusCode();
    }

    public async Task<Stream> DownloadFileAsync(Attachment attachment)
    {
        // Request signed download URL from your backend
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.example.com/attachments/{attachment.Id}/download-url");
        req.Headers.Authorization = new("Bearer", getAuthToken());

        var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<DownloadUrlResponse>()
            ?? throw new InvalidOperationException("missing body");

        // Download from S3 using signed URL - caller of DownloadFileAsync disposes the returned stream.
        return await http.GetStreamAsync(body.DownloadUrl);
    }

    public async Task DeleteFileAsync(Attachment attachment)
    {
        // Delete via your backend (backend handles S3 deletion); 404 is treated as success.
        var req = new HttpRequestMessage(HttpMethod.Delete,
            $"https://api.example.com/attachments/{attachment.Id}");
        req.Headers.Authorization = new("Bearer", getAuthToken());
        var resp = await http.SendAsync(req);
        if (resp.StatusCode != HttpStatusCode.NotFound)
        {
            resp.EnsureSuccessStatusCode();
        }
    }

    private sealed record UploadUrlResponse(string UploadUrl);
    private sealed record DownloadUrlResponse(string DownloadUrl);
}
```

> **Security Note:** Always use your backend to generate signed URLs and validate permissions. Never expose storage credentials to the client.

## API Reference

### AttachmentQueue

Main class for managing attachment synchronization.

#### Constructor

```csharp
new AttachmentQueue(AttachmentQueueOptions options)
```

**Options:**

| Parameter | Type | Required | Default | Description |
| ----------- | ------ | ---------- | --------- | ------------- |
| `Db` | `PowerSyncDatabase` | Yes | - | PowerSync database instance |
| `RemoteStorage` | `IRemoteStorageAdapter` | Yes | - | Remote storage adapter implementation |
| `LocalStorage` | `ILocalStorageAdapter` | Yes | - | Local storage adapter implementation |
| `WatchAttachments` | `Func<CancellationToken, IAsyncEnumerable<WatchedAttachmentItem[]>>` | Yes | - | Callback to determine which attachments to handle by the queue from your user defined query. |
| `TableName` | `string` | No | `"attachments"` | Name of the attachments table |
| `Logger` | `ILogger?` | No | `NullLogger` | Logger instance for diagnostic output |
| `SyncInterval` | `TimeSpan` | No | `30s` | Periodic polling interval for retrying failed uploads/downloads. A timer that calls `SyncStorageAsync()` on this cadence, ensuring operations are retried even if no database changes occur (e.g., after coming back online). |
| `SyncThrottle` | `TimeSpan` | No | `30ms` | Throttle duration for the reactive watch query on the attachments table. When attachment records change (e.g., a new file is queued), a watch query detects the change and triggers a sync. This throttle prevents the sync from firing too rapidly when many changes happen in quick succession (e.g., bulk inserts). This is distinct from `SyncInterval` — it controls how quickly the queue *reacts* to changes, while `SyncInterval` controls how often it *polls* for retries. |
| `DownloadAttachments` | `bool` | No | `true` | Whether to automatically download remote attachments |
| `ArchivedCacheLimit` | `int` | No | `100` | Maximum number of archived attachments before cleanup |
| `ErrorHandler` | `IAttachmentErrorHandler?` | No | `null` | Custom error handler for upload/download/delete operations |

#### Methods

##### `StartSyncAsync()`

Starts automatic attachment synchronization.

```csharp
await queue.StartSyncAsync();
```

This will:

- Initialize local storage
- Set up periodic sync based on `SyncInterval`
- Watch for changes in active attachments
- Process queued uploads, downloads, and deletes

##### `StopSyncAsync()`

Stops automatic attachment synchronization.

```csharp
await queue.StopSyncAsync();
```

##### `SaveFileAsync(...)`

Saves a file locally and queues it for upload to remote storage.

```csharp
var attachment = await queue.SaveFileAsync(
    data: stream,
    fileExtension: "pdf",
    mediaType: "application/pdf",
    id: "custom-id",                                // optional
    metaData: "{\"description\": \"Invoice\"}",     // optional
    updateHook: async (tx, attachment) =>
    {
        // Update your data model in the same transaction
        await tx.Execute(
            "INSERT INTO documents (id, attachment_id) VALUES (?, ?)",
            [documentId, attachment.Id]);
    });
```

**Parameters:**

| Parameter | Type | Required | Description |
| ----------- | ------ | ---------- | ------------- |
| `data` | `Stream` | Yes | File data stream |
| `fileExtension` | `string` | Yes | File extension (e.g., `"jpg"`, `"pdf"`) |
| `mediaType` | `string?` | No | MIME type (e.g., `"image/jpeg"`) |
| `id` | `string?` | No | Custom attachment ID (UUID generated if not provided) |
| `metaData` | `string?` | No | Optional metadata JSON string |
| `updateHook` | `Func<ITransaction, Attachment, Task>?` | No | Callback to update your data model atomically |

**Returns:** `Task<Attachment>` - The created attachment record

The `updateHook` is executed in the same database transaction as the attachment creation, ensuring atomic operations. This is the recommended way to link attachments to your data model.

##### `DeleteFileAsync(...)`

Deletes an attachment from both local and remote storage.

```csharp
await queue.DeleteFileAsync(
    id: attachmentId,
    updateHook: async (tx, attachment) =>
    {
        // Update your data model in the same transaction
        await tx.Execute(
            "UPDATE users SET photo_id = NULL WHERE photo_id = ?",
            [attachment.Id]);
    });
```

**Parameters:**

| Parameter | Type | Required | Description |
| ----------- | ------ | ---------- | ------------- |
| `id` | `string` | Yes | Attachment ID to delete |
| `updateHook` | `Func<ITransaction, Attachment, Task>?` | No | Callback to update your data model atomically |

##### `GenerateAttachmentIdAsync()`

Generates a new UUID for an attachment using SQLite's `uuid()` function.

```csharp
var id = await queue.GenerateAttachmentIdAsync();
```

**Returns:** `Task<string>` - A new UUID

##### `SyncStorageAsync()`

Manually triggers a sync operation. This is called automatically at regular intervals, but can be invoked manually if needed. For a non-awaiting "fire and forget" trigger, use `TriggerSync()`.

```csharp
await queue.SyncStorageAsync();
```

##### `TriggerSync()`

Requests a sync pass to run as soon as possible without waiting for it. Useful from error handlers ("retry now") or UI ("sync now"). Coalesces with any in-flight or pending sync via the throttle/buffer; safe to call rapidly.

```csharp
queue.TriggerSync();
```

**Returns:** `bool` - `true` if the trigger was buffered; `false` if sync isn't running, or if a trigger is already buffered (the channel collapses duplicates).

##### `ExpireCacheAsync()`

Removes archived attachments past `ArchivedCacheLimit` (and their local files). Archived rows up to the limit are kept as a cache so that briefly re-referenced attachments can be restored without a re-download.

```csharp
await queue.ExpireCacheAsync();
```

Useful to call manually when you want to immediately reclaim disk space (otherwise the cache is bounded but not actively pruned).

##### `ClearQueueAsync()`

Clears the attachment queue and deletes all attachment files from local storage. Useful on sign-out, account switch, or full reset. Does not affect remote storage.

```csharp
await queue.ClearQueueAsync();
```

##### `VerifyAttachmentsAsync()`

Verifies the integrity of all attachment records and repairs inconsistencies. Checks each attachment against local storage and:

- Updates `LocalUri` if file exists at a different path
- Archives attachments with missing local files that haven't been uploaded
- Requeues synced attachments for download if local files are missing

```csharp
await queue.VerifyAttachmentsAsync();
```

This is automatically called when `StartSyncAsync()` is invoked.

##### `WatchAttachments` callback

The `WatchAttachments` callback is a required option that tells the AttachmentQueue which attachments to handle. This tells the queue which attachments to download, upload, or archive.

**Signature:**

```csharp
Func<CancellationToken, IAsyncEnumerable<WatchedAttachmentItem[]>>
```

The callback receives the queue's lifecycle token; pass it into `PowerSyncDatabase.Watch<T>()` as `SQLWatchOptions.Signal` so `StopSyncAsync()` can actually stop the watcher.

**WatchedAttachmentItem:**

```csharp
public sealed record WatchedAttachmentItem
{
    public WatchedAttachmentItem(
        string id,
        string? filename = null,
        string? fileExtension = null,
        string? metaData = null);

    public string Id { get; }
    public string? Filename { get; }       // e.g., "document.pdf"
    public string? FileExtension { get; }  // e.g., "jpg", "pdf"
    public string? MetaData { get; }
}
```

Use either `FileExtension` OR `Filename`, not both.

**Example:**

```csharp
WatchAttachments = ct => WatchPhotos(db, ct);

static async IAsyncEnumerable<WatchedAttachmentItem[]> WatchPhotos(
    PowerSyncDatabase db,
    [EnumeratorCancellation] CancellationToken ct)
{
    var stream = db.Watch<UserPhotoRow>(
        "SELECT photo_id FROM users WHERE photo_id IS NOT NULL",
        null,
        new SQLWatchOptions { TriggerImmediately = true, Signal = ct });

    await foreach (var rows in stream.WithCancellation(ct))
    {
        yield return [.. rows.Select(r => new WatchedAttachmentItem(
            r.photo_id,
            fileExtension: "jpg"))];
    }
}
```

---

### Attachment

The attachment table is registered with PowerSync via `new Table(typeof(Attachment))`. The `Attachment` class itself carries the `[Table]` and `[Column]` attributes that produce a local-only schema with snake_case columns.

```csharp
[Table(TableName, LocalOnly = true, InsertOnly = false)]
public sealed class Attachment
{
    /// <summary>The attachment table name.</summary>
    public const string TableName = "attachments";

    [Column("id")]         public string Id { get; set; } = string.Empty;
    [Column("filename")]   public string Filename { get; set; } = string.Empty;
    [Column("state")]      public AttachmentState State { get; set; }
    [Column("local_uri")]  public string? LocalUri { get; set; }
    [Column("size")]       public long? Size { get; set; }
    [Column("media_type")] public string? MediaType { get; set; }
    [Column("timestamp")]  public long Timestamp { get; set; }
    [Column("meta_data")]  public string? MetaData { get; set; }
    [Column("has_synced")] public bool HasSynced { get; set; }
}
```

#### Schema registration

```csharp
var schema = new Schema(users, new Table(typeof(Attachment)));
```

`new Table(typeof(Attachment))` reads the `[Table]` and `[Column]` attributes and registers the Dapper type-map automatically - no separate factory or schema helper required. The schema table name is hard-wired to `"attachments"` via the `[Table]` attribute on `Attachment`; if you need a different name, define your own row type with the same columns and pass its name through `AttachmentQueueOptions.TableName`.

#### Columns

| Column | C# Property | SQLite Type | Description |
| -------- | ------------- | ------------- | ----------- |
| `id` | `Id` | `TEXT` | Attachment ID (primary key) |
| `filename` | `Filename` | `TEXT` | Filename with extension |
| `state` | `State` | `INTEGER` | Sync state (see `AttachmentState`) |
| `local_uri` | `LocalUri` | `TEXT` | Local file path or URI |
| `size` | `Size` | `INTEGER` | File size in bytes |
| `media_type` | `MediaType` | `TEXT` | MIME type |
| `timestamp` | `Timestamp` | `INTEGER` | Last update timestamp (Unix ms) |
| `meta_data` | `MetaData` | `TEXT` | Optional metadata JSON string |
| `has_synced` | `HasSynced` | `INTEGER` | Whether the file has synced (0 or 1) |

---

### AttachmentState

Enum representing attachment synchronization states.

```csharp
public enum AttachmentState
{
    QueuedUpload = 0,    // Queued for upload
    QueuedDownload = 1,  // Queued for download
    QueuedDelete = 2,    // Queued for deletion
    Synced = 3,          // Successfully synced
    Archived = 4,        // No longer referenced (orphaned)
}
```

---

### ILocalStorageAdapter

Interface for local file storage operations.

```csharp
public interface ILocalStorageAdapter
{
    Task InitializeAsync();
    Task ClearAsync();
    string GetLocalUri(string filename);
    Task<long> SaveFileAsync(string filePath, Stream data);
    Task<Stream> ReadFileAsync(string filePath);
    Task DeleteFileAsync(string filePath);
    Task<bool> FileExistsAsync(string filePath);
    Task CreateDirectoryAsync(string path);
    Task RemoveDirectoryAsync(string path);
}
```

---

### IRemoteStorageAdapter

Interface for remote storage operations.

```csharp
public interface IRemoteStorageAdapter
{
    Task UploadFileAsync(Stream fileData, Attachment attachment);
    Task<Stream> DownloadFileAsync(Attachment attachment);
    Task DeleteFileAsync(Attachment attachment);
}
```

---

### FileManagerLocalStorage

Default `ILocalStorageAdapter` over `System.IO.File`.

**Constructor:**

```csharp
new FileManagerLocalStorage(string attachmentsDirectory)
```

- `attachmentsDirectory` (required): Directory path under which attachment files are stored. The directory is created on `InitializeAsync()`.

## Error Handling

The `IAttachmentErrorHandler` interface allows you to customize error handling for sync operations.

### Interface

```csharp
public interface IAttachmentErrorHandler
{
    Task<bool> OnDownloadErrorAsync(Attachment attachment, Exception error);
    Task<bool> OnUploadErrorAsync(Attachment attachment, Exception error);
    Task<bool> OnDeleteErrorAsync(Attachment attachment, Exception error);
}
```

Each method returns:

- `true` to retry the operation
- `false` to archive the attachment and skip retrying

### Example

```csharp
public sealed class MyErrorHandler(ILogger logger) : IAttachmentErrorHandler
{
    public Task<bool> OnDownloadErrorAsync(Attachment attachment, Exception error)
    {
        logger.LogError(error, "Download failed for {Filename}", attachment.Filename);

        // Retry on network errors, archive on 404s
        if (error is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
        {
            logger.LogInformation("File not found, archiving attachment");
            return Task.FromResult(false); // Archive
        }

        logger.LogInformation("Will retry download on next sync");
        return Task.FromResult(true); // Retry
    }

    public Task<bool> OnUploadErrorAsync(Attachment attachment, Exception error)
    {
        logger.LogError(error, "Upload failed for {Filename}", attachment.Filename);

        // Always retry uploads
        return Task.FromResult(true);
    }

    public Task<bool> OnDeleteErrorAsync(Attachment attachment, Exception error)
    {
        logger.LogError(error, "Delete failed for {Filename}", attachment.Filename);

        // Retry deletes, but archive after too many attempts
        var attempts = attachment.MetaData is null
            ? 0
            : JsonSerializer.Deserialize<DeleteState>(attachment.MetaData)?.DeleteAttempts ?? 0;

        return Task.FromResult(attempts < 3); // Retry up to 3 times
    }

    private sealed record DeleteState(int DeleteAttempts);
}

var queue = new AttachmentQueue(new AttachmentQueueOptions
{
    // ... other options
    ErrorHandler = new MyErrorHandler(logger),
});
```

## Advanced Usage

### Verification and Recovery

The `VerifyAttachmentsAsync()` method checks attachment integrity and repairs issues:

```csharp
// Manually verify all attachments
await queue.VerifyAttachmentsAsync();
```

This is useful if:

- Local files may have been manually deleted
- Storage paths changed
- You suspect data inconsistencies

Verification is automatically run when `StartSyncAsync()` is called.

### Custom Sync Intervals

Adjust sync frequency based on your needs:

```csharp
var queue = new AttachmentQueue(new AttachmentQueueOptions
{
    // ... other options
    SyncInterval = TimeSpan.FromSeconds(60), // Poll for retries every 60 seconds instead of 30
    SyncThrottle = TimeSpan.FromMilliseconds(100), // React to attachment changes within 100ms (default: 30ms)
});
```

- **`SyncInterval`** controls the periodic polling timer — how often the queue retries failed operations.
- **`SyncThrottle`** controls how quickly the queue reacts to attachment table changes. The default (30ms) is fast enough for most use cases. Increase it if you see performance issues during bulk attachment operations.

### Archive and Cache Management

Control how many archived attachments are kept before cleanup:

```csharp
var queue = new AttachmentQueue(new AttachmentQueueOptions
{
    // ... other options
    ArchivedCacheLimit = 200, // Keep up to 200 archived attachments
});
```

Archived attachments are those no longer referenced in your data model but not yet deleted. This allows for:

- Quick restoration if references are added back
- Caching of recently used files
- Gradual cleanup to avoid storage bloat

When the limit is reached, the oldest archived attachments are permanently deleted.

## License

Apache 2.0
