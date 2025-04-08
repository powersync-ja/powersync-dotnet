using dotenv.net;

namespace CommandLine.Utils;

public class Config
{
    public string SupabaseUrl { get; set; }
    public string SupabaseAnonKey { get; set; }
    public string PowerSyncUrl { get; set; }
    public string BackendUrl { get; set; }
    public string SupabaseUsername { get; set; }
    public string SupabasePassword { get; set; }
    public bool UseSupabase { get; set; }

    public Config()
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
        BackendUrl = Environment.GetEnvironmentVariable("BACKEND_URL")
                      ?? throw new InvalidOperationException("BACKEND_URL environment variable is not set.");
        SupabaseUsername = Environment.GetEnvironmentVariable("SUPABASE_USERNAME")
                           ?? throw new InvalidOperationException("SUPABASE_USERNAME environment variable is not set.");
        SupabasePassword = Environment.GetEnvironmentVariable("SUPABASE_PASSWORD")
                           ?? throw new InvalidOperationException("SUPABASE_PASSWORD environment variable is not set.");

        // Parse boolean value
        string useSupabaseStr = Environment.GetEnvironmentVariable("USE_SUPABASE") ?? "false";
        if (!bool.TryParse(useSupabaseStr, out bool useSupabase))
        {
            throw new InvalidOperationException("USE_SUPABASE environment variable is not a valid boolean.");
        }
        UseSupabase = useSupabase;
    }
}