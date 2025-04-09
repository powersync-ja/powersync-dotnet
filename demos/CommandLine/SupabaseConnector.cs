namespace CommandLine;

using CommandLine.Helpers;
using CommandLine.Models.Supabase;
using CommandLine.Utils;
using Newtonsoft.Json;
using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.DB.Crud;
using Supabase;
using Supabase.Gotrue;
using Supabase.Postgrest.Exceptions;
using Supabase.Postgrest.Interfaces;

public class SupabaseConnector : IPowerSyncBackendConnector
{
    private readonly Supabase.Client _supabase;
    private readonly Config _config;
    private Session? _currentSession;

    public Session? CurrentSession
    {
        get => _currentSession;
        set
        {
            _currentSession = value;

            if (_currentSession?.User?.Id != null)
            {
                UserId = _currentSession.User.Id;
            }
        }
    }

    public string UserId { get; private set; } = "";

    public bool Ready { get; private set; }

    public SupabaseConnector(Config config)
    {
        _config = config;
        _supabase = new Supabase.Client(config.SupabaseUrl, config.SupabaseAnonKey, new SupabaseOptions
        {
            AutoConnectRealtime = true
        });

        _ = _supabase.InitializeAsync();
    }

    public async Task Login(string email, string password)
    {
        var response = await _supabase.Auth.SignInWithPassword(email, password);
        if (response?.User == null || response.AccessToken == null)
        {
            throw new Exception("Login failed.");
        }

        CurrentSession = response;
    }

    public Task<PowerSyncCredentials?> FetchCredentials()
    {
        PowerSyncCredentials? credentials = null;

        var sessionResponse = _supabase.Auth.CurrentSession;
        if (sessionResponse?.AccessToken != null)
        {
            credentials = new PowerSyncCredentials(_config.PowerSyncUrl, sessionResponse.AccessToken);
        }

        return Task.FromResult(credentials);
    }

    public async Task UploadData(IPowerSyncDatabase database)
    {
        var transaction = await database.GetNextCrudTransaction();
        if (transaction == null) return;

        try
        {
            foreach (var op in transaction.Crud)
            {
                switch (op.Op)
                {
                    case UpdateType.PUT:
                        if (op.Table.ToLower().Trim() == "lists")
                        {
                            var model = JsonConvert.DeserializeObject<List>(JsonConvert.SerializeObject(op.OpData)) ?? throw new InvalidOperationException("Model is null.");
                            model.Id = op.Id;

                            await _supabase.From<List>().Upsert(model);
                        }
                        else if (op.Table.ToLower().Trim() == "todos")
                        {
                            var model = JsonConvert.DeserializeObject<Todo>(JsonConvert.SerializeObject(op.OpData)) ?? throw new InvalidOperationException("Model is null.");
                            model.Id = op.Id;

                            await _supabase.From<Todo>().Upsert(model);
                        }
                        break;

                    case UpdateType.PATCH:
                        if (op.OpData is null || op.OpData.Count == 0)
                        {
                            Console.WriteLine("PATCH skipped: No data to update.");
                            break;
                        }

                        if (op.Table.ToLower().Trim() == "lists")
                        {
                            // Create an update query for the 'Todo' table where the 'Id' matches 'op.Id'
                            IPostgrestTable<List> updateQuery = _supabase
                            .From<List>()
                            .Where(x => x.Id == op.Id);

                            // Loop through each key-value pair in the operation data (op.OpData) to apply updates dynamically
                            foreach (var kvp in op.OpData)
                            {
                                // Apply the "SET" operation for each key-value pair. 
                                // The key represents the JSON property name and the value is the new value to be set
                                updateQuery = SupabasePatchHelper.ApplySet(updateQuery, kvp.Key, kvp.Value);
                            }

                            _ = await updateQuery.Update();
                        }
                        else if (op.Table.ToLower().Trim() == "todos")
                        {
                            // Create an update query for the 'Todo' table where the 'Id' matches 'op.Id'
                            IPostgrestTable<Todo> updateQuery = _supabase
                                .From<Todo>()
                                .Where(x => x.Id == op.Id);

                            // Loop through each key-value pair in the operation data (op.OpData) to apply updates dynamically
                            foreach (var kvp in op.OpData)
                            {
                                // Apply the "SET" operation for each key-value pair. 
                                // The key represents the JSON property name and the value is the new value to be set
                                updateQuery = SupabasePatchHelper.ApplySet(updateQuery, kvp.Key, kvp.Value);
                            }

                            _ = await updateQuery.Update();
                        }
                        break;

                    case UpdateType.DELETE:
                        if (op.Table.ToLower().Trim() == "lists")
                        {
                            await _supabase
                            .From<List>()
                            .Where(x => x.Id == op.Id)
                            .Delete();
                        }
                        else if (op.Table.ToLower().Trim() == "todos")
                        {
                            await _supabase
                            .From<Todo>()
                            .Where(x => x.Id == op.Id)
                            .Delete();
                        }
                        break;

                    default:
                        throw new InvalidOperationException("Unknown operation type.");
                }
            }

            await transaction.Complete();
        }
        catch (PostgrestException ex)
        {
            Console.WriteLine($"Error during upload: {ex.Message}");
            throw;
        }
    }
}