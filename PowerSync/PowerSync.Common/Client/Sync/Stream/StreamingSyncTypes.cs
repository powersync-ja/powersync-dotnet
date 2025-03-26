namespace PowerSync.Common.Client.Sync.Stream;

using PowerSync.Common.Client.Sync.Bucket;
using PowerSync.Common.DB.Crud;
using Newtonsoft.Json;

public class ContinueCheckpointRequest
{
    [JsonProperty("buckets")]
    public List<BucketRequest> Buckets { get; set; } = new();

    [JsonProperty("checkpoint_token")]
    public string CheckpointToken { get; set; } = "";

    [JsonProperty("limit")]
    public int? Limit { get; set; }
}

public class SyncNewCheckpointRequest
{
    [JsonProperty("buckets")]
    public List<BucketRequest>? Buckets { get; set; }

    [JsonProperty("request_checkpoint")]
    public RequestCheckpoint RequestCheckpoint { get; set; } = new();

    [JsonProperty("limit")]
    public int? Limit { get; set; }
}

public class RequestCheckpoint
{
    [JsonProperty("include_data")]
    public bool IncludeData { get; set; }

    [JsonProperty("include_checksum")]
    public bool IncludeChecksum { get; set; }
}

public class SyncResponse
{
    [JsonProperty("data")]
    public List<SyncDataBucketJSON>? Data { get; set; }

    [JsonProperty("has_more")]
    public bool HasMore { get; set; }

    [JsonProperty("checkpoint_token")]
    public string? CheckpointToken { get; set; }

    [JsonProperty("checkpoint")]
    public Checkpoint? Checkpoint { get; set; }
}

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

public abstract class StreamingSyncLine { }

public class StreamingSyncCheckpoint : StreamingSyncLine
{
    [JsonProperty("checkpoint")]
    public Checkpoint Checkpoint { get; set; } = new();
}

public class StreamingSyncCheckpointDiff : StreamingSyncLine
{
    [JsonProperty("checkpoint_diff")]
    public CheckpointDiff CheckpointDiff { get; set; } = new();
}

public class CheckpointDiff
{
    [JsonProperty("last_op_id")]
    public string LastOpId { get; set; } = "";

    [JsonProperty("updated_buckets")]
    public List<BucketChecksum> UpdatedBuckets { get; set; } = new();

    [JsonProperty("removed_buckets")]
    public List<string> RemovedBuckets { get; set; } = new();

    [JsonProperty("write_checkpoint")]
    public string WriteCheckpoint { get; set; } = "";
}

public class StreamingSyncDataJSON : StreamingSyncLine
{
    [JsonProperty("data")]
    public SyncDataBucketJSON Data { get; set; } = new();
}

public class StreamingSyncCheckpointComplete : StreamingSyncLine
{
    [JsonProperty("checkpoint_complete")]
    public CheckpointComplete CheckpointComplete { get; set; } = new();
}

public class CheckpointComplete
{
    [JsonProperty("last_op_id")]
    public string LastOpId { get; set; } = "";
}

public class StreamingSyncKeepalive : StreamingSyncLine
{
    [JsonProperty("token_expires_in")]
    public int? TokenExpiresIn { get; set; }
}

public class CrudRequest
{
    [JsonProperty("data")]
    public List<CrudEntry> Data { get; set; } = new();
}

public class CrudResponse
{
    [JsonProperty("checkpoint")]
    public string? Checkpoint { get; set; }
}
