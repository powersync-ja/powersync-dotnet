using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using PowerSync.Common.DB.Crud;

namespace PowerSync.Common.Client.Sync.Stream;

/// <summary>
/// An internal instruction emitted by the sync client in the core extension in response to the
/// SDK passing sync data into the extension.
/// </summary>
public abstract class Instruction
{

    public static Instruction[] ParseInstructions(string rawResponse)
    {
        var jsonArray = JArray.Parse(rawResponse);
        List<Instruction> instructions = [];

        foreach (JObject item in jsonArray)
        {
            var instruction = ParseInstruction(item);
            if (instruction == null)
            {
                throw new JsonSerializationException("Failed to parse instruction from JSON.");
            }
            instructions.Add(instruction);
        }

        return instructions.ToArray();
    }

    public static Instruction? ParseInstruction(JObject json)
    {
        if (json.ContainsKey("LogLine"))
            return json["LogLine"]!.ToObject<LogLine>();
        if (json.ContainsKey("UpdateSyncStatus"))
            return json["UpdateSyncStatus"]!.ToObject<UpdateSyncStatus>();
        if (json.ContainsKey("EstablishSyncStream"))
            return json["EstablishSyncStream"]!.ToObject<EstablishSyncStream>();
        if (json.ContainsKey("FetchCredentials"))
            return json["FetchCredentials"]!.ToObject<FetchCredentials>();
        if (json.ContainsKey("CloseSyncStream"))
            return new CloseSyncStream();
        if (json.ContainsKey("FlushFileSystem"))
            return new FlushFileSystem();
        if (json.ContainsKey("DidCompleteSync"))
            return new DidCompleteSync();

        throw new JsonSerializationException("Unknown Instruction type.");
    }
}

public class LogLine : Instruction
{
    [JsonProperty("severity")]
    public string Severity { get; set; } = null!;  // "DEBUG", "INFO", "WARNING"

    [JsonProperty("line")]
    public string Line { get; set; } = null!;
}

public class EstablishSyncStream : Instruction
{
    [JsonProperty("request")]
    public StreamingSyncRequest Request { get; set; } = null!;
}

public class UpdateSyncStatus : Instruction
{
    [JsonProperty("status")]
    public CoreSyncStatus Status { get; set; } = null!;
}

public class CoreSyncStatus
{
    [JsonProperty("connected")]
    public bool Connected { get; set; }

    [JsonProperty("connecting")]
    public bool Connecting { get; set; }

    [JsonProperty("priority_status")]
    public List<SyncPriorityStatus> PriorityStatus { get; set; } = [];

    [JsonProperty("downloading")]
    public DownloadProgress? Downloading { get; set; }

}

public class SyncPriorityStatus
{
    [JsonProperty("priority")]
    public int Priority { get; set; }

    [JsonProperty("last_synced_at")]
    public long LastSyncedAt { get; set; }

    [JsonProperty("has_synced")]
    public bool? HasSynced { get; set; }
}

public class DownloadProgress
{
    [JsonProperty("buckets")]
    public Dictionary<string, BucketProgress> Buckets { get; set; } = null!;
}

public class BucketProgress
{
    [JsonProperty("priority")]
    public int Priority { get; set; }

    [JsonProperty("at_last")]
    public int AtLast { get; set; }

    [JsonProperty("since_last")]
    public int SinceLast { get; set; }

    [JsonProperty("target_count")]
    public int TargetCount { get; set; }
}

public class FetchCredentials : Instruction
{
    [JsonProperty("did_expire")]
    public bool DidExpire { get; set; }
}

public class CloseSyncStream : Instruction { }
public class FlushFileSystem : Instruction { }
public class DidCompleteSync : Instruction { }

public class CoreInstructionHelpers
{
    public static DB.Crud.SyncPriorityStatus PriorityToStatus(SyncPriorityStatus status)
    {
        return new DB.Crud.SyncPriorityStatus
        {
            Priority = status.Priority,
            HasSynced = status.HasSynced ?? null,
            LastSyncedAt = status?.LastSyncedAt != null ? new DateTime(status!.LastSyncedAt) : null
        };
    }

    public static DB.Crud.SyncStatusOptions CoreStatusToSyncStatus(CoreSyncStatus status)
    {
        var coreCompleteSync =
            status.PriorityStatus.FirstOrDefault(s => s.Priority == SyncProgress.FULL_SYNC_PRIORITY);
        var completeSync = coreCompleteSync != null ? PriorityToStatus(coreCompleteSync) : null;

        return new DB.Crud.SyncStatusOptions
        {
            Connected = status.Connected,
            Connecting = status.Connecting,
            DataFlow = new DB.Crud.SyncDataFlowStatus
            {
                // We expose downloading as a boolean field, the core extension reports download information as a nullable
                // download status. When that status is non-null, a download is in progress.
                Downloading = status.Downloading != null,
                DownloadProgress = status.Downloading?.Buckets
            },
            LastSyncedAt = completeSync?.LastSyncedAt,
            HasSynced = completeSync?.HasSynced,
            PriorityStatusEntries = status.PriorityStatus.Select(PriorityToStatus).ToArray()
        };
    }
}