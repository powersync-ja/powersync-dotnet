namespace Common.Client.Sync.Bucket;

using Newtonsoft.Json;

public class OplogEntryJSON
{
    [JsonProperty("checksum")]
    public int Checksum { get; set; }

    [JsonProperty("data")]
    public string? Data { get; set; }

    [JsonProperty("object_id")]
    public string? ObjectId { get; set; }

    [JsonProperty("object_type")]
    public string? ObjectType { get; set; }

    [JsonProperty("op_id")]
    public string OpId { get; set; } = null!;

    [JsonProperty("op")]
    public string Op { get; set; } = null!;

    [JsonProperty("subkey")]
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
            row.Subkey is string subkey ? subkey : JsonConvert.SerializeObject(row.Subkey),
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
            Subkey = JsonConvert.SerializeObject(Subkey)
        };
    }
}