namespace Common.DB.Crud;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum UpdateType
{
    [JsonPropertyName("PUT")]
    PUT,

    [JsonPropertyName("PATCH")]
    PATCH,

    [JsonPropertyName("DELETE")]
    DELETE
}

public class CrudEntryJSON
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("data")]
    public string Data { get; set; } = null!;

    [JsonPropertyName("tx_id")]
    public int? TransactionId { get; set; }
}

public class CrudEntryDataJSON
{
    [JsonPropertyName("data")]
    public Dictionary<string, object> Data { get; set; } = new();

    [JsonPropertyName("op")]
    public UpdateType Op { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;
}

public class CrudEntryOutputJSON
{
    [JsonPropertyName("op_id")]
    public int OpId { get; set; }

    [JsonPropertyName("op")]
    public UpdateType Op { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("tx_id")]
    public int? TransactionId { get; set; }

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }
}

public class CrudEntry(int clientId, UpdateType op, string table, string id, int? transactionId = null, Dictionary<string, object>? opData = null)
{
    public int ClientId { get; private set; } = clientId;
    public string Id { get; private set; } = id;
    public UpdateType Op { get; private set; } = op;
    public Dictionary<string, object>? OpData { get; private set; } = opData;
    public string Table { get; private set; } = table;
    public int? TransactionId { get; private set; } = transactionId;

    public static CrudEntry FromRow(CrudEntryJSON dbRow)
    {
        var data = JsonSerializer.Deserialize<CrudEntryDataJSON>(dbRow.Data)!;
        return new CrudEntry(
            int.Parse(dbRow.Id),
            data.Op,
            data.Type,
            data.Id,
            dbRow.TransactionId,
            data.Data
        );
    }

    public CrudEntryOutputJSON ToJSON()
    {
        return new CrudEntryOutputJSON
        {
            OpId = ClientId,
            Op = Op,
            Type = Table,
            Id = Id,
            TransactionId = TransactionId,
            Data = OpData
        };
    }

    public bool Equals(CrudEntry other)
    {
        return JsonSerializer.Serialize(ToComparisonArray()) == JsonSerializer.Serialize(other.ToComparisonArray());
    }

    public override int GetHashCode()
    {
        return JsonSerializer.Serialize(ToComparisonArray()).GetHashCode();
    }

    private object[] ToComparisonArray()
    {
        return [TransactionId ?? 0, ClientId, Op, Table, Id, OpData ?? []];
    }
}