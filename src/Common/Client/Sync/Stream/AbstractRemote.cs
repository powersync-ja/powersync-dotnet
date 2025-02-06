namespace Common.Client.Sync.Stream;

using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Client.Connection;
using Newtonsoft.Json;

public class AbstractRemote
{
    private static int REFRESH_CREDENTIALS_SAFETY_PERIOD_MS = 30_000;
    private readonly HttpClient httpClient;
    protected IPowerSyncBackendConnector connector;

    protected PowerSyncCredentials? credentials;

    public AbstractRemote(HttpClient httpClient, IPowerSyncBackendConnector connector)
    {
        this.httpClient = httpClient;
        this.connector = connector;
    }

    public async Task<PowerSyncCredentials?> GetCredentialsAsync()
    {
        if (credentials?.ExpiresAt > DateTime.UtcNow.AddMilliseconds(REFRESH_CREDENTIALS_SAFETY_PERIOD_MS))
        {
            return credentials;
        }

        credentials = await connector.FetchCredentials();

        // TODO CL trailing forward slash check
        return credentials;
    }

    string getUserAgent()
    {
        return "powersync-js/" + "unversioned-dotnet";
    }

    public async Task<Stream> PostStream(SyncStreamOptions options, CancellationToken cancellationToken)
    {
        var request = await BuildRequest(options.Path);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(JsonConvert.SerializeObject(options.Data), Encoding.UTF8, "application/json")
        };

        foreach (var header in options.Headers)
        {
            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode || response.Content == null)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {response.StatusCode}: {errorText}");
        }

        var stream = await response.Content.ReadAsStreamAsync();
        return stream;
    }

    private async Task<RequestDetails> BuildRequest(string path)
    {
        var credentials = await GetCredentialsAsync();

        if (credentials == null || string.IsNullOrEmpty(credentials.Endpoint))
        {
            throw new InvalidOperationException("PowerSync endpoint not configured");
        }

        if (string.IsNullOrEmpty(credentials.Token))
        {
            var error = new HttpRequestException("Not signed in");
            throw error;
        }

        var userAgent = getUserAgent();

        return new RequestDetails
        {
            Url = credentials.Endpoint + path,
            Headers = new Dictionary<string, string>
        {
            { "content-type", "application/json" },
            { "Authorization", $"Token {credentials.Token}" },
            { "x-user-agent", userAgent }
        }
        };
    }
}

public class SyncStreamOptions
{
    public string Path { get; set; } = "";
    public object Data { get; set; } = new();
    public Dictionary<string, string> Headers { get; set; } = new();
}

public class RequestDetails
{
    public string Url { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();
}
