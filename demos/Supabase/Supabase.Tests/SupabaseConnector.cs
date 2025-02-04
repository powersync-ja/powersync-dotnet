using Common.Client;
using Common.Client.Connection;
using Common.DB.Crud;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace Supabase.Tests;


public class SupabaseConnector : IPowerSyncBackendConnector
{

    static readonly string SupabaseUrl = "";
    static readonly string SupabaseAnonKey = "";
    static readonly string PowerSyncUrl = "";

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
        public string Name { get; set; }

        [Column("country_id")]
        public int CountryId { get; set; }

        //... etc.
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

    public async Task UploadData(AbstractPowerSyncDatabase database)
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