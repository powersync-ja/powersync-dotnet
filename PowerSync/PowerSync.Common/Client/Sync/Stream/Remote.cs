namespace PowerSync.Common.Client.Sync.Stream;

using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using PowerSync.Common.Client.Connection;

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
    private readonly HttpClient httpClient;
    protected IPowerSyncBackendConnector connector;

    protected PowerSyncCredentials? credentials;

    public Remote(IPowerSyncBackendConnector connector)
    {
        httpClient = new HttpClient();
        this.connector = connector;
    }

    /// <summary>
    /// Get credentials currently cached, or fetch new credentials if none are available.
    /// These credentials may have expired already.
    /// </summary>
    public async Task<PowerSyncCredentials?> GetCredentials()
    {
        if (credentials != null)
        {
            return credentials;
        }
        return await PrefetchCredentials();
    }

    /// <summary>
    /// Fetch a new set of credentials and cache it.
    /// Until this call succeeds, GetCredentials will still return the old credentials.
    /// This may be called before the current credentials have expired.
    /// </summary>
    public async Task<PowerSyncCredentials?> PrefetchCredentials()
    {
        credentials = await FetchCredentials();
        return credentials;
    }

    /// <summary>
    /// Get credentials for PowerSync.
    /// This should always fetch a fresh set of credentials - don't use cached values.
    /// </summary>
    public async Task<PowerSyncCredentials?> FetchCredentials()
    {
        return await connector.FetchCredentials();
    }

    /// <summary>
    /// Immediately invalidate credentials.
    /// This may be called when the current credentials have expired.
    /// </summary>
    public void InvalidateCredentials()
    {
        credentials = null;
    }

    static string GetUserAgent()
    {
        object[] attributes = Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);

        string fullInformationalVersion = attributes.Length == 0 ? "" : ((AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion;

        // Remove the build metadata part (anything after the '+')
        int plusIndex = fullInformationalVersion.IndexOf('+');
        string version = plusIndex >= 0
            ? fullInformationalVersion.Substring(0, plusIndex)
            : fullInformationalVersion;

        return $"powersync-dotnet/{version}";
    }

    public async Task<T> Get<T>(string path, Dictionary<string, string>? headers = null)
    {
        var request = await BuildRequest(HttpMethod.Get, path, data: null, additionalHeaders: headers);

        using var client = new HttpClient();
        var response = await client.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            InvalidateCredentials();
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Received {response.StatusCode} - {response.ReasonPhrase} when getting from {path}: {errorMessage}");
        }

        var responseData = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<T>(responseData)!;
    }

    public async IAsyncEnumerable<StreamingSyncLine?> PostStream(SyncStreamOptions options)
    {
        using var requestMessage = await BuildRequest(HttpMethod.Post, options.Path, options.Data, options.Headers);
        using var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, options.CancellationToken);

        if (response.Content == null)
        {
            throw new HttpRequestException($"HTTP {response.StatusCode}: No content");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            InvalidateCredentials();
        }

        if (!response.IsSuccessStatusCode)
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

    private async Task<HttpRequestMessage> BuildRequest(HttpMethod method, string path, object? data = null, Dictionary<string, string>? additionalHeaders = null)
    {
        var credentials = await GetCredentials();

        if (credentials == null || string.IsNullOrEmpty(credentials.Endpoint))
        {
            throw new InvalidOperationException("PowerSync endpoint not configured");
        }

        if (string.IsNullOrEmpty(credentials.Token))
        {
            var error = new HttpRequestException("Not signed in");
            throw error;
        }

        var userAgent = GetUserAgent();

        var request = new HttpRequestMessage(method, credentials.Endpoint + path)
        {
            Content = data != null ?
                new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json")
                : null
        };

        request.Headers.TryAddWithoutValidation("content-type", "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", $"Token {credentials.Token}");
        request.Headers.TryAddWithoutValidation("x-user-agent", userAgent);

        if (additionalHeaders != null)
        {
            foreach (var header in additionalHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return request;
    }
}
