namespace PowerSync.Common.Client.Connection;
public class PowerSyncCredentials(string endpoint, string token, DateTime? expiresAt = null)
{
    public string Endpoint { get; set; } = endpoint;
    public string Token { get; set; } = token;
    public DateTime? ExpiresAt { get; set; } = expiresAt;
}