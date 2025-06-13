using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.DB.Crud;

namespace TodoSQLite.Data;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class NodeConnector : IPowerSyncBackendConnector
{
    private readonly HttpClient httpClient;

    public string BackendUrl { get; }
    public string PowerSyncUrl { get; }
    public string UserId { get; }
    private string? clientId;

    public NodeConnector()
    {
        httpClient = new HttpClient();

        // Load or generate User ID
        UserId = LoadOrGenerateUserId();

        BackendUrl = "http://localhost:6060";
        PowerSyncUrl = "http://localhost:8080";

        clientId = null;
    }

    public string LoadOrGenerateUserId()
    {
        return "8ba3ec38-6cc8-449c-88c0-9275987ea5d2";
    }

    public async Task<PowerSyncCredentials?> FetchCredentials()
    {
        var tokenEndpoint = "api/auth/token";
        var url = $"{BackendUrl}/{tokenEndpoint}?user_id={UserId}";

        var response = await httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Received {response.StatusCode} from {tokenEndpoint}: {await response.Content.ReadAsStringAsync()}");
        }

        string responseBody = await response.Content.ReadAsStringAsync();
        var jsonResponse = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);

        if (jsonResponse == null || !jsonResponse.ContainsKey("token"))
        {
            throw new Exception("Invalid response received from authentication endpoint.");
        }

        return new PowerSyncCredentials(PowerSyncUrl, jsonResponse["token"]);
    }

    public async Task UploadData(IPowerSyncDatabase database)
    {
        CrudTransaction? transaction;
        try
        {
            transaction = await database.GetNextCrudTransaction();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UploadData Error: {ex.Message}");
            return;
        }

        if (transaction == null)
        {
            return;
        }

        clientId ??= await database.GetClientId();

        try
        {
            var batch = new List<object>();

            foreach (var operation in transaction.Crud)
            {
                batch.Add(new
                {
                    op = operation.Op.ToString(),
                    table = operation.Table,
                    id = operation.Id,
                    data = operation.OpData
                });
            }

            var payload = JsonSerializer.Serialize(new { batch });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await httpClient.PostAsync($"{BackendUrl}/api/data", content);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Received {response.StatusCode} from /api/data: {await response.Content.ReadAsStringAsync()}");
            }

            await transaction.Complete();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UploadData Error: {ex.Message}");
            throw;
        }
    }
}
