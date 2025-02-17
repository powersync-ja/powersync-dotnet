using Common.Client.Sync.Stream;
using Newtonsoft.Json;
using Supabase.Storage;

namespace Supabase.Tests;

public class SupabaseConnectorTests
{
    // [Fact]
    // public async void Connector()
    // {
    //     Console.WriteLine("Supabase Connector Test");
    //     new SupabaseConnector();
    // }

    [Fact]
    public async void StreamTest()
    {
        Console.WriteLine("Supabase Stream Test");
        var connector = new SupabaseConnector();
        await connector.Login();

        Remote remote = new(connector);
        // var creds = await connector.FetchCredentials();
        var creds = await remote.GetCredentials();

        var cts = new CancellationTokenSource(); // Equivalent to `AbortController` in TS

        Console.WriteLine("Starting stream...");
        var syncOptions = new SyncStreamOptions
        {
            Path = "/sync/stream",
            CancellationToken = cts.Token,
            Data = new StreamingSyncRequest
            {
                Buckets = [],  // Replace `new object()` with actual data
                IncludeChecksum = true,
                RawData = true,
                Parameters = new Dictionary<string, object>(), // Replace with actual params
                ClientId = "cfe62ca2-8a28-495d-9b46-20991b5c2ac3"
            }
        };

        await remote.PostStream(syncOptions);
        Console.WriteLine("XXXX---completed");
    }
}