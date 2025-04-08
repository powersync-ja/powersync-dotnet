namespace CommandLine.Utils;

using dotenv.net;

public class SupabaseConfig
{
    public string SupabaseUrl { get; set; }
    public string SupabaseAnonKey { get; set; }
    public string PowerSyncUrl { get; set; }

    public SupabaseConfig()
    {
        DotEnv.Load();
        Console.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
        // Retrieve the environment variables
        SupabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")
                      ?? throw new InvalidOperationException("SUPABASE_URL environment variable is not set.");
        SupabaseAnonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY")
                          ?? throw new InvalidOperationException("SUPABASE_ANON_KEY environment variable is not set.");
        PowerSyncUrl = Environment.GetEnvironmentVariable("POWERSYNC_URL")
                       ?? throw new InvalidOperationException("POWERSYNC_URL environment variable is not set.");
    }
}