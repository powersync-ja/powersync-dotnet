namespace PowerSync.Common.IntegrationTests;


using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;
using PowerSync.Common.DB.Crud;


public class NodeConnector : IPowerSyncBackendConnector
{
    private readonly HttpClient _httpClient;

    public string BackendUrl { get; }
    public string PowerSyncUrl { get; }
    public string UserId { get; private set; }
    private string? clientId;

    public NodeConnector(string userId)
    {
        _httpClient = new HttpClient();

        // Load or generate User ID
        UserId = userId;

        BackendUrl = "http://localhost:6060";
        PowerSyncUrl = "http://localhost:8080";

        clientId = null;
    }

    public async Task<PowerSyncCredentials?> FetchCredentials()
    {
        string tokenEndpoint = "api/auth/token";
        string url = $"{BackendUrl}/{tokenEndpoint}?user_id={UserId}";

        HttpResponseMessage response = await _httpClient.GetAsync(url);
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

            HttpResponseMessage response = await _httpClient.PostAsync($"{BackendUrl}/api/data", content);

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
