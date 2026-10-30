namespace PowerSync.Common.Client.Sync.Stream;

using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

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
    private readonly HttpClient _httpClient;
    private readonly ICredentialsProvider _credentials;

    public Remote(IAuthenticator authenticator, string? endpoint = null)
    {
        if (authenticator is IPowerSyncBackendConnector connector)
        {
            _credentials = new ConnectorCredentialsProvider(connector);
        }
        else
        {
            if (endpoint is null)
            {
                throw new ArgumentException("endpoint is required when not using IPowerSyncBackendConnector.");
            }
            _credentials = new AuthenticatorCredentialsProvider(endpoint, authenticator);
        }

        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Get credentials currently cached, or fetch new credentials if none are available.
    /// These credentials may have expired already.
    /// </summary>
    public Task<PowerSyncCredentials?> GetCredentials()
    {
        return _credentials.GetCredentials();
    }

    /// <summary>
    /// Fetch a new set of credentials and cache it.
    /// Until this call succeeds, GetCredentials will still return the old credentials.
    /// This may be called before the current credentials have expired.
    /// </summary>
    public Task<PowerSyncCredentials?> PrefetchCredentials()
    {
        return _credentials.PrefetchCredentials();
    }

    /// <summary>
    /// Immediately invalidate credentials.
    /// This may be called when the current credentials have expired.
    /// </summary>
    public void InvalidateCredentials()
    {
        _credentials.InvalidateCredentials();
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

    public virtual async Task<T> FetchJson<T>(string path, HttpMethod? method = null, object? data = null, Dictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        var request = await BuildRequest(method ?? HttpMethod.Get, path, data, headers);

        var response = await _httpClient.SendAsync(request, ct);

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

    /// <summary>
    /// Posts to the stream endpoint and returns a raw NDJSON stream that can be read line by line.
    /// </summary>
    public virtual async Task<Stream> PostStreamRaw(SyncStreamOptions options)
    {
        var request = await BuildRequest(HttpMethod.Post, options.Path, options.Data, options.Headers);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, options.CancellationToken);

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

        return await response.Content.ReadAsStreamAsync();
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

interface ICredentialsProvider
{
    Task<PowerSyncCredentials?> GetCredentials();
    Task<PowerSyncCredentials?> PrefetchCredentials();
    void InvalidateCredentials();
}

/// <summary>
/// Wraps an <see cref="IAuthenticator" /> without caching credentials, since <see cref="IAuthenticator.ResolveCredentials" />
/// is expected to handle caching itself.
/// </summary>
class AuthenticatorCredentialsProvider(string endpoint, IAuthenticator authenticator) : ICredentialsProvider
{
    private readonly string _endpoint = endpoint;
    private readonly IAuthenticator _authenticator = authenticator;

    public async Task<PowerSyncCredentials?> GetCredentials()
    {
        string token = await _authenticator.ResolveCredentials();
        return new PowerSyncCredentials(_endpoint, token);
    }

    public Task<PowerSyncCredentials?> PrefetchCredentials()
    {
        _authenticator.InvalidateCredentials();
        return GetCredentials();
    }

    public void InvalidateCredentials()
    {
        _authenticator.InvalidateCredentials();
    }
}

/// <summary>
/// Wraps an <see cref="IPowerSyncBackendConnector" /> to provide credentials caching behaviour.
/// </summary>
class ConnectorCredentialsProvider(IPowerSyncBackendConnector connector) : ICredentialsProvider
{
    private readonly IPowerSyncBackendConnector _connector = connector;
    private readonly SemaphoreSlim _fetchingCredentials = new(1, 1);
    private volatile PowerSyncCredentials? _cachedCredentials;

    public async Task<PowerSyncCredentials?> GetCredentials()
    {
        if (_cachedCredentials is { } cached)
        {
            return cached;
        }

        await _fetchingCredentials.WaitAsync();
        try
        {
            // With concurrent calls, it's possible that credentials have just been fetched.
            return _cachedCredentials ?? await FetchAndCacheCredentials();
        }
        finally
        {
            _fetchingCredentials.Release();
        }
    }

    /// <summary>
    /// If no other task is currently fetching credentials, fetches and caches a new set. The previous
    /// credentials stay cached until the fetch succeeds.
    /// </summary>
    public async Task<PowerSyncCredentials?> PrefetchCredentials()
    {
        if (!await _fetchingCredentials.WaitAsync(0))
        {
            return _cachedCredentials;
        }

        try
        {
            return await FetchAndCacheCredentials();
        }
        finally
        {
            _fetchingCredentials.Release();
        }
    }

    private async Task<PowerSyncCredentials?> FetchAndCacheCredentials()
    {
        var credentials = await _connector.FetchCredentials();
        _cachedCredentials = credentials;
        return credentials;
    }

    public void InvalidateCredentials()
    {
        _cachedCredentials = null;
    }
}
