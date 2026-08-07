using PowerSync.Common.Attachments;

namespace UnoTodo.Attachments;

public class NodeRemoteStorageAdapter(HttpClient client, string backendUrl) : IRemoteStorageAdapter
{
    private readonly HttpClient _client = client;
    private readonly string _backendUrl = backendUrl;

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
