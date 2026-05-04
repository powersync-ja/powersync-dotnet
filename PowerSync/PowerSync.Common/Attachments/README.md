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

Default adapter backed by `System.IO.File`.

```csharp
using PowerSync.Common.Attachments;

var localStorage = new FileManagerLocalStorage("./attachments");
```

#### Custom Local Storage Adapter

Implement [`ILocalStorageAdapter`](./ILocalStorageAdapter.cs) for other environments (e.g. an in-memory adapter for tests).

### Remote Storage Adapter

Implement [`IRemoteStorageAdapter`](./IRemoteStorageAdapter.cs) to communicate with your cloud storage (S3, Supabase Storage, Cloudflare R2, Azure Blob, etc.).

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

## Error Handling

Implement [`IAttachmentErrorHandler`](./IAttachmentErrorHandler.cs) to customize how upload, download, and delete failures are handled.

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
    SyncThrottle = TimeSpan.FromMilliseconds(100), // Minimum 100ms between consecutive sync passes (default: 30ms)
});
```

- **`SyncInterval`** controls the periodic polling timer — how often the queue retries failed operations.
- **`SyncThrottle`** caps how frequently sync passes can run. Increase if you see contention during bulk operations.

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
