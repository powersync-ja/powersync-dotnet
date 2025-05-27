namespace PowerSync.Common.DB.Crud;

using System.Collections.Generic;
using Newtonsoft.Json;

public enum UpdateType
{
    [JsonProperty("PUT")] PUT,

    [JsonProperty("PATCH")] PATCH,

    [JsonProperty("DELETE")] DELETE
}

public class CrudEntryJSON
{
    [JsonProperty("id")] public string Id { get; set; } = null!;

    [JsonProperty("data")] public string Data { get; set; } = null!;
    
    [JsonProperty("tx_id")] public long? TransactionId { get; set; }
}

public class CrudEntryDataJSON
{
    [JsonProperty("data")] public Dictionary<string, object>? Data { get; set; }
    
    [JsonProperty("old")] public Dictionary<string, string?>? Old { get; set; }
    
    [JsonProperty("op")] public UpdateType Op { get; set; }
    
    [JsonProperty("type")] public string Type { get; set; } = null!;
    
    [JsonProperty("id")] public string Id { get; set; } = null!;
    
    [JsonProperty("metadata")] public string? Metadata { get; set; }
}

public class CrudEntryOutputJSON
{
    [JsonProperty("op_id")] public int OpId { get; set; }

    [JsonProperty("op")] public UpdateType Op { get; set; }

    [JsonProperty("type")] public string Type { get; set; } = null!;

    [JsonProperty("id")] public string Id { get; set; } = null!;

    [JsonProperty("tx_id")] public long? TransactionId { get; set; }

    [JsonProperty("data")] public Dictionary<string, object>? Data { get; set; }
}

public class CrudEntry(
    int clientId,
    UpdateType op,
    string table,
    string id,
    long? transactionId = null,
    Dictionary<string, object>? opData = null,
    Dictionary<String, String?>? previousValues = null,
    string? metadata = null
)
{
    public int ClientId { get; private set; } = clientId;
    public string Id { get; private set; } = id;
    public UpdateType Op { get; private set; } = op;
    public Dictionary<string, object>? OpData { get; private set; } = opData;
    public string Table { get; private set; } = table;
    public long? TransactionId { get; private set; } = transactionId;

    /// <summary>
    /// Previous values before this change.
    /// </summary>
    public Dictionary<String, String?>? PreviousValues { get; private set; } = previousValues;

    /// <summary>
    /// Client-side metadata attached with this write.
    ///
    /// This field is only available when the `trackMetadata` option was set to `true` when creating a table
    /// and the insert or update statement set the `_metadata` column.
    /// </summary>
    public string? Metadata { get; private set; } = metadata;

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
            data.Data,
            data.Old,
            data.Metadata
        );
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