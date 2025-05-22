
namespace PowerSync.Common.Client.Sync.Bucket;

using System;
using System.Threading.Tasks;

using Newtonsoft.Json;
using PowerSync.Common.DB.Crud;
using PowerSync.Common.Utils;

public class Checkpoint
{
    [JsonProperty("last_op_id")]
    public string LastOpId { get; set; } = null!;

    [JsonProperty("buckets")]
    public BucketChecksum[] Buckets { get; set; } = [];

    [JsonProperty("write_checkpoint")]
    public string? WriteCheckpoint { get; set; } = null;
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
    
    Task<CrudEntry?> NextCrudItem();
    Task<bool> HasCrud();
    Task<CrudBatch?> GetCrudBatch(int limit = 100);

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