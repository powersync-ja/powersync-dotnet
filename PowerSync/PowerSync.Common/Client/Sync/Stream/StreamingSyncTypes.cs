namespace PowerSync.Common.Client.Sync.Stream;

using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.DB.Crud;
using Newtonsoft.Json;

public class StreamingSyncRequest
{
    [JsonProperty("buckets")]
    public List<BucketRequest>? Buckets { get; set; }

    [JsonProperty("only")]
    public List<string>? Only { get; set; } = [];

    [JsonProperty("include_checksum")]
    public bool IncludeChecksum { get; set; }

    [JsonProperty("raw_data")]
    public bool RawData { get; set; }

    [JsonProperty("parameters")]
    public Dictionary<string, object>? Parameters { get; set; }

    [JsonProperty("client_id")]
    public string? ClientId { get; set; }
}

public class BucketRequest
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("after")]
    public string After { get; set; } = "";
}