namespace MAUITodo.Data;

using MAUITodo.Models;
using MAUITodo.Config;
using MAUITodo.Helpers;

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
    private readonly EnvConfig _envConfig;
    private Session? _currentSession;

    public Session? CurrentSession
    {
        get => _currentSession;
        set
        {
            _currentSession = value;

            if (_currentSession?.User?.Id != null)
            {
                UserID = _currentSession.User.Id;
            }
        }
    }

    public string UserID { get; private set; } = "";

    public bool Ready { get; private set; }

    public SupabaseConnector(EnvConfig envConfig)
    {
        _envConfig = envConfig;
        _supabase = new Supabase.Client(envConfig.SupabaseUrl, envConfig.SupabaseKey, new SupabaseOptions
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
            credentials = new PowerSyncCredentials(_envConfig.PowerSyncUrl, sessionResponse.AccessToken);
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
                            var model = JsonConvert.DeserializeObject<TodoList>(JsonConvert.SerializeObject(op.OpData)) ?? throw new InvalidOperationException("Model is null.");
                            model.ID = op.Id;

                            await _supabase.From<TodoList>().Upsert(model);
                        }
                        else if (op.Table.ToLower().Trim() == "todos")
                        {
                            var model = JsonConvert.DeserializeObject<TodoItem>(JsonConvert.SerializeObject(op.OpData)) ?? throw new InvalidOperationException("Model is null.");
                            model.ID = op.Id;

                            await _supabase.From<TodoItem>().Upsert(model);
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
                            // Create an update query for the 'TodoItem' table where the 'ID' matches 'op.Id'
                            IPostgrestTable<TodoList> updateQuery = _supabase
                            .From<TodoList>()
                            .Where(x => x.ID == op.Id);

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
                            // Create an update query for the 'TodoItem' table where the 'ID' matches 'op.Id'
                            IPostgrestTable<TodoItem> updateQuery = _supabase
                                .From<TodoItem>()
                                .Where(x => x.ID == op.Id);

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
                            .From<TodoList>()
                            .Where(x => x.ID == op.Id)
                            .Delete();
                        }
                        else if (op.Table.ToLower().Trim() == "todos")
                        {
                            await _supabase
                            .From<TodoItem>()
                            .Where(x => x.ID == op.Id)
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
