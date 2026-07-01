using PowerSync.Common.Attachments;

namespace MAUITodo.Attachments;

public class NodeRemoteStorageAdapter : IRemoteStorageAdapter
{
    private readonly HttpClient _client;
    private readonly string _backendUrl;

    public NodeRemoteStorageAdapter(HttpClient client, string backendUrl)
    {
        _client = client;
        _backendUrl = backendUrl;
    }

    public async Task UploadFileAsync(Stream fileData, Attachment attachment)
    {
        var content = new StreamContent(fileData);
        content.Headers.ContentType = new(attachment.MediaType ?? "application/octet-stream");
        var response = await _client.PutAsync($"{_backendUrl}/api/attachments/{attachment.Id}", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Stream> DownloadFileAsync(Attachment attachment)
    {
        var response = await _client.GetAsync($"{_backendUrl}/api/attachments/{attachment.Id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Stream.Null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    public async Task DeleteFileAsync(Attachment attachment)
    {
        var response = await _client.DeleteAsync($"{_backendUrl}/api/attachments/{attachment.Id}");
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }
}
