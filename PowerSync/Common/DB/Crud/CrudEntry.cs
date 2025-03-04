namespace Common.DB.Crud;

using System.Collections.Generic;
using Newtonsoft.Json;

public enum UpdateType
{
    [JsonProperty("PUT")]
    PUT,

    [JsonProperty("PATCH")]
    PATCH,

    [JsonProperty("DELETE")]
    DELETE
}

public class CrudEntryJSON
{
    [JsonProperty("id")]
    public string Id { get; set; } = null!;

    [JsonProperty("data")]
    public string Data { get; set; } = null!;

    [JsonProperty("tx_id")]
    public long? TransactionId { get; set; }
}

public class CrudEntryDataJSON
{
    [JsonProperty("data")]
    public Dictionary<string, object> Data { get; set; } = new();

    [JsonProperty("op")]
    public UpdateType Op { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; } = null!;

    [JsonProperty("id")]
    public string Id { get; set; } = null!;
}

public class CrudEntryOutputJSON
{
    [JsonProperty("op_id")]
    public int OpId { get; set; }

    [JsonProperty("op")]
    public UpdateType Op { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; } = null!;

    [JsonProperty("id")]
    public string Id { get; set; } = null!;

    [JsonProperty("tx_id")]
    public long? TransactionId { get; set; }

    [JsonProperty("data")]
    public Dictionary<string, object>? Data { get; set; }
}

public class CrudEntry(int clientId, UpdateType op, string table, string id, long? transactionId = null, Dictionary<string, object>? opData = null)
{
    public int ClientId { get; private set; } = clientId;
    public string Id { get; private set; } = id;
    public UpdateType Op { get; private set; } = op;
    public Dictionary<string, object>? OpData { get; private set; } = opData;
    public string Table { get; private set; } = table;
    public long? TransactionId { get; private set; } = transactionId;

    public static CrudEntry FromRow(CrudEntryJSON dbRow)
    {
        var data = JsonConvert.DeserializeObject<CrudEntryDataJSON>(dbRow.Data)
                   ?? throw new JsonException("Invalid JSON format in CrudEntryJSON data.");

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

    public override bool Equals(object? obj)
    {
        if (obj is not CrudEntry other) return false;
        return JsonConvert.SerializeObject(this) == JsonConvert.SerializeObject(other);
    }

    public override int GetHashCode()
    {
        return JsonConvert.SerializeObject(this).GetHashCode();
    }
}