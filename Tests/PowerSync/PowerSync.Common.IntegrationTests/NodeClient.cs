namespace PowerSync.Common.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using PowerSync.Common.DB.Crud;

public class NodeClient
{
    private readonly HttpClient _httpClient;
    private readonly string _backendUrl;
    private readonly string _userId;

    public NodeClient(string userId)
    {
        _httpClient = new HttpClient();
        _backendUrl = "http://localhost:6060";
        _userId = userId;
    }

    public NodeClient(string backendUrl, string userId)
    {
        _httpClient = new HttpClient();
        _backendUrl = backendUrl;
        _userId = userId;
    }

    public Task<string> CreateList(string id, string name)
    {
        return CreateItem("lists", id, name);
    }

    async Task<string> CreateItem(string table, string id, string name)
    {
        var data = new Dictionary<string, object>
        {
            { "created_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
            { "name", name },
            { "owner_id", _userId }
        };

        var batch = new[]
        {
            new
            {
                op = UpdateType.PUT.ToString(),
                table = table,
                id = id,
                data = data
            }
        };

        var payload = JsonSerializer.Serialize(new { batch });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _httpClient.PostAsync($"{_backendUrl}/api/data", content);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine(await response.Content.ReadAsStringAsync());
            throw new Exception(
                $"Failed to create item. Status: {response.StatusCode}, " +
                $"Response: {await response.Content.ReadAsStringAsync()}"
            );
        }

        return await response.Content.ReadAsStringAsync();
    }

    public Task<string> DeleteList(string id)
    {
        return DeleteItem("lists", id);
    }

    public Task<string> CreateTodo(string id, string listId, string description)
    {
        return CreateTodoItem("todos", id, listId, description);
    }

    async Task<string> CreateTodoItem(string table, string id, string listId, string description)
    {
        var data = new Dictionary<string, object>
        {
            { "created_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
            { "description", description },
            { "list_id", listId },
            { "created_by", _userId },
            { "completed", 0 },
        };

        var batch = new[]
        {
            new
            {
                op = UpdateType.PUT.ToString(),
                table = table,
                id = id,
                data = data
            }
        };

        var payload = JsonSerializer.Serialize(new { batch });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _httpClient.PostAsync($"{_backendUrl}/api/data", content);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine(await response.Content.ReadAsStringAsync());
            throw new Exception(
                $"Failed to create todo. Status: {response.StatusCode}, " +
                $"Response: {await response.Content.ReadAsStringAsync()}"
            );
        }

        return await response.Content.ReadAsStringAsync();
    }

    public Task<string> DeleteTodo(string id)
    {
        return DeleteItem("todos", id);
    }

    async Task<string> DeleteItem(string table, string id)
    {
        var batch = new[]
        {
            new
            {
                op = UpdateType.DELETE.ToString(),
                table = table,
                id = id
            }
        };

        var payload = JsonSerializer.Serialize(new { batch });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _httpClient.PostAsync($"{_backendUrl}/api/data", content);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine(await response.Content.ReadAsStringAsync());
            throw new Exception(
                $"Failed to delete item. Status: {response.StatusCode}, " +
                $"Response: {await response.Content.ReadAsStringAsync()}"
            );
        }

        return await response.Content.ReadAsStringAsync();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
