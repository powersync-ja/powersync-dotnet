using dotenv.net;

namespace CommandLine.Utils;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
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

        // Parse boolean value first
        string useSupabaseStr = Environment.GetEnvironmentVariable("USE_SUPABASE") ?? "false";
        if (!bool.TryParse(useSupabaseStr, out bool useSupabase))
        {
            throw new InvalidOperationException("USE_SUPABASE environment variable is not a valid boolean.");
        }
        UseSupabase = useSupabase;

        Console.WriteLine("Use Supabase: " + UseSupabase);

        PowerSyncUrl = GetRequiredEnv("POWERSYNC_URL");

        if (UseSupabase)
        {
            SupabaseUrl = GetRequiredEnv("SUPABASE_URL");
            SupabaseAnonKey = GetRequiredEnv("SUPABASE_ANON_KEY");
            SupabaseUsername = GetRequiredEnv("SUPABASE_USERNAME");
            SupabasePassword = GetRequiredEnv("SUPABASE_PASSWORD");
        }
        else
        {
            BackendUrl = GetRequiredEnv("BACKEND_URL");
        }
    }

    private static string GetRequiredEnv(string key)
    {
        return Environment.GetEnvironmentVariable(key)
               ?? throw new InvalidOperationException($"{key} environment variable is not set.");
    }
}