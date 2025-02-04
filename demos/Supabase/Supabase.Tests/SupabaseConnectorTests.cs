namespace Supabase.Tests;

public class SupabaseConnectorTests
{
    [Fact]
    public async void Connector()
    {
        Console.WriteLine("SchemaTest");
        new SupabaseConnector();
    }

}