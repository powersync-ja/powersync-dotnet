namespace MAUITodo.Data;

using PowerSync.Common.Attachments;
using Supabase.Storage;

public class SupabaseRemoteStorageAdapter : IRemoteStorageAdapter
{
    private readonly Supabase.Client _client;
    private readonly string _bucketId;

    public SupabaseRemoteStorageAdapter(Supabase.Client client, string bucketId)
    {
        _client = client;
        _bucketId = bucketId;
    }

    public async Task UploadFileAsync(Stream stream, Attachment attachment)
    {
        // Convert Stream into byte[] for Supabase
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            bytes = ms.ToArray();
        }

        string mediaType = attachment.MediaType ?? "application/octet-stream";
        await _client.Storage
            .From(_bucketId)
            .Upload(bytes, attachment.Filename, new FileOptions { ContentType = mediaType });
    }

    public async Task<Stream> DownloadFileAsync(Attachment attachment)
    {
        var bytes = await _client.Storage
            .From(_bucketId)
            .Download(attachment.Filename, null); // Pass null manually to force specific overload to be used

        return new MemoryStream(bytes);
    }

    public async Task DeleteFileAsync(Attachment attachment)
    {
        await _client.Storage.From(_bucketId).Remove(attachment.Filename);
    }
}
