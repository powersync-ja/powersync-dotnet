namespace Common.Client.Sync.Stream;

using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Client.Connection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class SyncStreamOptions
{
    public string Path { get; set; } = "";
    public StreamingSyncRequest Data { get; set; } = new();
    public Dictionary<string, string> Headers { get; set; } = new();

    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}

public class RequestDetails
{
    public string Url { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();
}

public class Remote
{
    private static int REFRESH_CREDENTIALS_SAFETY_PERIOD_MS = 30_000;
    private readonly HttpClient httpClient;
    protected IPowerSyncBackendConnector connector;

    protected PowerSyncCredentials? credentials;

    public Remote(IPowerSyncBackendConnector connector)
    {
        // TODO CL This can be passed in as a parameter
        this.httpClient = new HttpClient();
        this.connector = connector;
    }

    public async Task<PowerSyncCredentials?> GetCredentials()
    {
        if (credentials?.ExpiresAt > DateTime.UtcNow.AddMilliseconds(REFRESH_CREDENTIALS_SAFETY_PERIOD_MS))
        {
            return credentials;
        }

        credentials = await connector.FetchCredentials();

        // TODO CL trailing forward slash check
        return credentials;
    }

    static string GetUserAgent()
    {
        object[] attributes = Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);

        string fullInformationalVersion = attributes.Length == 0 ? "" : ((AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion;

        // Remove the build metadata part (anything after the '+')
        int plusIndex = fullInformationalVersion.IndexOf('+');
        string version = plusIndex >= 0
            ? fullInformationalVersion[..plusIndex]
            : fullInformationalVersion;

        return $"powersync-dotnet/{version}";
    }

    public async IAsyncEnumerable<StreamingSyncLine?> PostStream(SyncStreamOptions options)
    {
        var request = await BuildRequest(options.Path);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(JsonConvert.SerializeObject(options.Data), Encoding.UTF8, "application/json"),
        };

        // Add the built headers
        foreach (var header in request.Headers)
        {
            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Add the options headers
        foreach (var header in options.Headers)
        {
            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, options.CancellationToken);

        if (!response.IsSuccessStatusCode || response.Content == null)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {response.StatusCode}: {errorText}");
        }

        var stream = await response.Content.ReadAsStreamAsync();

        // Read NDJSON stream
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            yield return ParseStreamingSyncLine(JObject.Parse(line));
        }
    }

    public static StreamingSyncLine? ParseStreamingSyncLine(JObject json)
    {
        // Determine the type based on available keys
        if (json.ContainsKey("checkpoint"))
        {
            return json.ToObject<StreamingSyncCheckpoint>();
        }
        else if (json.ContainsKey("checkpoint_diff"))
        {
            return json.ToObject<StreamingSyncCheckpointDiff>();
        }
        else if (json.ContainsKey("checkpoint_complete"))
        {
            return json.ToObject<StreamingSyncCheckpointComplete>();
        }
        else if (json.ContainsKey("data"))
        {
            return json.ToObject<StreamingSyncDataJSON>();
        }
        else if (json.ContainsKey("token_expires_in"))
        {
            return json.ToObject<StreamingSyncKeepalive>();
        }
        else
        {
            return null;
        }
    }

    private async Task<RequestDetails> BuildRequest(string path)
    {
        var credentials = await GetCredentials();

        if (credentials == null || string.IsNullOrEmpty(credentials.Endpoint))
        {
            throw new InvalidOperationException("PowerSync endpoint not configured");
        }

        if (string.IsNullOrEmpty(credentials.Token))
        {
            // TODO CL error status code 401
            var error = new HttpRequestException("Not signed in");
            throw error;
        }

        var userAgent = GetUserAgent();

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
