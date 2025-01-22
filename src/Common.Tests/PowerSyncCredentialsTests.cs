namespace Common.Tests;


using Common.Client.Connection;
using Common.MicrosoftDataSqlite;


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

    [Fact]
    public async void LoadVersion()
    {
        var db = new MDSAdapter();
        // Console.WriteLine("Post adapter");
        // var x = await db.writeConnection.Execute("SELECT powersync_rs_version() as version");
        // Console.WriteLine(x.Rows.Array.First().First().Value);

        // var x = await db.Execute("SELECT powersync_rs_version() as version");
        // using var connection = new SqliteConnection("Data Source=:memory:");
        // var db = new MDSConnection(new MDSConnectionOptions(connection));
        // connection.Open();

        // string extensionPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../libpowersync.dylib");

        // connection.LoadExtension(extensionPath);

        // var x = await db.Execute("SELECT powersync_rs_version() as version where 1 = 0;");
        // var x = await db.Execute("SELECT * FROM Users WHERE 1 = 0;");


        // Console.WriteLine(x.Rows.Array.First().First().Value);
        // new AbstractPowerSyncDatabase();
        // await Task.Delay(5000);
    }

}