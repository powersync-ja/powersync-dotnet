namespace Common.Client.Sync.Bucket;

using System.Text.Json;
using System.Text.Json.Serialization;


public class OplogEntryJSON
{
    [JsonPropertyName("checksum")]
    public int Checksum { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("object_id")]
    public string? ObjectId { get; set; }

    [JsonPropertyName("object_type")]
    public string? ObjectType { get; set; }

    [JsonPropertyName("op_id")]
    public string OpId { get; set; } = null!;

    [JsonPropertyName("op")]
    public string Op { get; set; } = null!;

    [JsonPropertyName("subkey")]
    public object? Subkey { get; set; }
}

public class OplogEntry(
    string opId,
    OpType op,
    int checksum,
    string subkey,
    string? objectType = null,
    string? objectId = null,
    string? data = null
    )
{
    public string OpId { get; private set; } = opId;
    public OpType Op { get; private set; } = op;
    public int Checksum { get; private set; } = checksum;
    public string Subkey { get; private set; } = subkey;
    public string? ObjectType { get; private set; } = objectType;
    public string? ObjectId { get; private set; } = objectId;
    public string? Data { get; private set; } = data;

    public static OplogEntry FromRow(OplogEntryJSON row)
    {
        return new OplogEntry(
            row.OpId,
            OpType.FromJSON(row.Op),
            row.Checksum,
            row.Subkey is string subkey ? subkey : JsonSerializer.Serialize(row.Subkey),
            row.ObjectType,
            row.ObjectId,
            row.Data
        );
    }

    public OplogEntryJSON ToJSON()
    {
        return new OplogEntryJSON
        {
            OpId = OpId,
            Op = Op.ToJSON(),
            Checksum = Checksum,
            Data = Data,
            ObjectType = ObjectType,
            ObjectId = ObjectId,
            Subkey = JsonSerializer.Serialize(Subkey)
        };
    }
}

