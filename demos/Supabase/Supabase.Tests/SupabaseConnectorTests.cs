namespace Supabase.Tests;

public class SupabaseConnectorTests
{
    [Fact]
    public async void Connector()
    {
        Console.WriteLine("Supabase Connector Test");
        new SupabaseConnector();
    }

}