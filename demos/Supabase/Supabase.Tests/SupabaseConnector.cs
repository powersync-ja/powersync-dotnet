namespace Supabase.Tests;

using Common.Client;
using Common.Client.Connection;
using Common.DB.Crud;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;


public class SupabaseConnector : IPowerSyncBackendConnector
{

    static readonly string SupabaseUrl = "https://jngrpbvcbzmwkgzvgjel.supabase.co";
    static readonly string SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImpuZ3JwYnZjYnptd2tnenZnamVsIiwicm9sZSI6ImFub24iLCJpYXQiOjE3MTgyNzEyMjEsImV4cCI6MjAzMzg0NzIyMX0.ZKknr7_y5W7yg4DtdfKmBfh2g9-L3aU0jznmXcNlEpU";
    static readonly string PowerSyncUrl = "https://67af2e42fa9789f5199a0949.powersync.staging.journeyapps.com";

    private Client Client;
    public SupabaseConnector()
    {
        var options = new Supabase.SupabaseOptions { };


        Client = new Supabase.Client(SupabaseUrl, SupabaseAnonKey, options);
        Console.WriteLine("Constructor!");
    }

    // TODO CL - change to todos, add dynamic handling based on table name
    [Table("cities")]
    class City : BaseModel
    {
        [PrimaryKey("id")]
        public int Id { get; set; }

        [Column("name")]
        public string? Name { get; set; }

        [Column("country_id")]
        public int CountryId { get; set; }

        //... etc.
    }

    public async Task Login()
    {
        await Client.Auth.SignInWithPassword("9@9.com", "9@9.com");
    }

    public async Task<PowerSyncCredentials?> FetchCredentials()
    {
        // TODO check for error?
        var session = await Client.Auth.RetrieveSessionAsync();
        if (session == null || session.Expired())
        {
            throw new Exception($"Could not fetch Supabase credentials: {session?.ToString()}");
        }

        Console.WriteLine($"Session expires at: {session.ExpiresAt()}");

        return new PowerSyncCredentials(PowerSyncUrl, session.AccessToken ?? "", session.ExpiresAt());
    }

    public async Task UploadData(IPowerSyncDatabase database)
    {
        // TODO CL
        // database.GetNextCrudTransaction

        // TODO CL define models?
        // TODO CL understand error handling

        CrudEntry? lastOp = null;
        CrudEntry[] batch = [];
        foreach (var op in batch)
        {
            lastOp = op;

            var table = Client.From<City>();
            var x = await table.Get();

            object? result = null;

            switch (op.Op)
            {
                case UpdateType.PUT:
                    var record = new Dictionary<string, object>(op.OpData) { ["id"] = op.Id };
                    // result = await table.Upsert(record);
                    break;

                case UpdateType.PATCH:
                    // result = await table.Update(op.OpData).Eq("id", op.Id);
                    break;

                case UpdateType.DELETE:
                    // result = await table.Delete().Eq("id", op.Id);
                    break;
            }

            // Check for errors
            // if (result is Postgrest.Responses.ModeledResponse<City> response && response.Error != null)
            // {
            //     Console.Error.WriteLine(response.Error.Message);
            //     throw new Exception($"Could not update Supabase. Received error: {response.Error.Message}");
            // }
            Console.WriteLine(op);
        }
    }
}