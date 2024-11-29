namespace Common.Tests;
using Common.Client.Connection;
public class PowerSyncCredentialsTests
{
    [Fact]
    public void SimpleTest()
    {
        var endpoint = "http://localhost";
        var token = "token";
        var expiresAt = new DateTime();
        PowerSyncCredentials credentials = new PowerSyncCredentials(endpoint, token, expiresAt);
        Assert.Equal(endpoint, credentials.Endpoint);
        Assert.Equal(token, credentials.Token);
        Assert.Equal(expiresAt, credentials.ExpiresAt);
    }
}