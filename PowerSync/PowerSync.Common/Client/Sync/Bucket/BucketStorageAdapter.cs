
namespace PowerSync.Common.Client.Sync.Bucket;

using System;
using System.Threading.Tasks;

using PowerSync.Common.DB.Crud;
using PowerSync.Common.Utils;
using Newtonsoft.Json;

public static class PowerSyncControlCommand
{
    public const string PROCESS_TEXT_LINE = "line_text";
    public const string PROCESS_BSON_LINE = "line_binary";
    public const string STOP = "stop";
    public const string START = "start";
    public const string NOTIFY_TOKEN_REFRESHED = "refreshed_token";
    public const string NOTIFY_CRUD_UPLOAD_COMPLETED = "completed_upload";
    public const string UPDATE_SUBSCRIPTIONS = "update_subscriptions";
}

public class Checkpoint
{
    [JsonProperty("last_op_id")]
    public string LastOpId { get; set; } = null!;

    [JsonProperty("buckets")]
    public BucketChecksum[] Buckets { get; set; } = [];

    [JsonProperty("write_checkpoint")]
    public string? WriteCheckpoint { get; set; } = null;
}

public class BucketState
{
    [JsonProperty("bucket")]
    public string Bucket { get; set; } = null!;

    [JsonProperty("op_id")]
    public string OpId { get; set; } = null!;
}

public class SyncLocalDatabaseResult
{
    [JsonProperty("ready")]
    public bool Ready { get; set; }

    [JsonProperty("checkpointValid")]
    public bool CheckpointValid { get; set; }

    [JsonProperty("checkpointFailures")]
    public string[]? CheckpointFailures { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj is not SyncLocalDatabaseResult other) return false;
        return JsonConvert.SerializeObject(this) == JsonConvert.SerializeObject(other);
    }

    public override int GetHashCode()
    {
        return JsonConvert.SerializeObject(this).GetHashCode();
    }
}

public class BucketChecksum
{
    [JsonProperty("bucket")]
    public string Bucket { get; set; } = null!;

    [JsonProperty("checksum")]
    public long Checksum { get; set; }

    /// <summary>
    /// Count of operations - informational only.
    /// </summary>
    [JsonProperty("count")]
    public int? Count { get; set; }
}

public static class PSInternalTable
{
    public static readonly string DATA = "ps_data";
    public static readonly string CRUD = "ps_crud";
    public static readonly string BUCKETS = "ps_buckets";
    public static readonly string OPLOG = "ps_oplog";
    public static readonly string UNTYPED = "ps_untyped";
}

public class BucketStorageEvent
{
    public bool CrudUpdate { get; set; }
}

public interface IBucketStorageAdapter : IEventStream<BucketStorageEvent>
{
    Task Init();
    Task SaveSyncData(SyncDataBatch batch);
    Task RemoveBuckets(string[] buckets);
    Task SetTargetCheckpoint(Checkpoint checkpoint);

    void StartSession();

    Task<BucketState[]> GetBucketStates();

    Task<SyncLocalDatabaseResult> SyncLocalDatabase(Checkpoint checkpoint);

    Task<CrudEntry?> NextCrudItem();
    Task<bool> HasCrud();
    Task<CrudBatch?> GetCrudBatch(int limit = 100);

    Task<bool> HasCompletedSync();
    Task<bool> UpdateLocalTarget(Func<Task<string>> callback);

    /// <summary>
    /// Exposed for tests only.
    /// </summary>
    Task AutoCompact();
    Task ForceCompact();

    string GetMaxOpId();

    /// <summary>
    /// Get a unique client ID.
    /// </summary>
    Task<string> GetClientId();

    /// <summary>
    /// Invokes the `powersync_control` function for the sync client.
    /// </summary>
    Task<string> Control(string op, object? payload);
}