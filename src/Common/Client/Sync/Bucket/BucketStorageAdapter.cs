
namespace Common.Client.Sync.Bucket;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Common.DB.Crud;
using Newtonsoft.Json;

public class Checkpoint
{
    [JsonProperty("last_op_id")]
    public string LastOpId { get; set; } = null!;

    [JsonProperty("buckets")]
    public List<BucketChecksum> Buckets { get; set; } = new();

    [JsonProperty("write_checkpoint")]
    public string? WriteCheckpoint { get; set; }
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
    public List<string>? CheckpointFailures { get; set; }
}

public class BucketChecksum
{
    [JsonProperty("bucket")]
    public string Bucket { get; set; } = null!;

    /// <summary>
    /// 32-bit unsigned hash.
    /// </summary>
    [JsonProperty("checksum")]
    public uint Checksum { get; set; }

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

public interface IBucketStorageAdapter : IDisposable
{
    Task Init();
    Task SaveSyncData(SyncDataBatch batch);
    Task RemoveBuckets(List<string> buckets);
    Task SetTargetCheckpoint(Checkpoint checkpoint);

    void StartSession();

    Task<List<BucketState>> GetBucketStates();

    Task<SyncLocalDatabaseResult> SyncLocalDatabase(Checkpoint checkpoint);

    Task<CrudEntry?> NextCrudItem();
    Task<bool> HasCrud();
    Task<CrudBatch?> GetCrudBatch(int? limit = null);

    Task<bool> HasCompletedSync();
    Task<bool> UpdateLocalTarget(Func<Task<string>> callback);

    // Exposed for tests only
    Task AutoCompact();
    Task ForceCompact();

    string GetMaxOpId();

    /// <summary>
    /// Get a unique client ID.
    /// </summary>
    Task<string> GetClientId();
}