using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using PowerSync.Common.DB.Crud;
using PowerSync.Common.Utils.Converters;

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
            instructions.Add(ParseInstruction(item));
        }

        return [.. instructions];
    }

    public static Instruction ParseInstruction(JObject json)
    {
        if (json.ContainsKey("LogLine"))
            return json["LogLine"]!.ToObject<LogLine>()!;
        if (json.ContainsKey("UpdateSyncStatus"))
            return json["UpdateSyncStatus"]!.ToObject<UpdateSyncStatus>()!;
        if (json.ContainsKey("EstablishSyncStream"))
            return json["EstablishSyncStream"]!.ToObject<EstablishSyncStream>()!;
        if (json.ContainsKey("FetchCredentials"))
            return json["FetchCredentials"]!.ToObject<FetchCredentials>()!;
        if (json.ContainsKey("CloseSyncStream"))
            return json["CloseSyncStream"]!.ToObject<CloseSyncStream>()!;
        if (json.ContainsKey("DidCompleteSync"))
            return new DidCompleteSync();

        return new UnknownSyncInstruction(json);
    }
}

/// <summary>
/// An <see cref="Instruction"/> that doesn't start or stop a sync iteration.
/// </summary>
public abstract class NonInterruptingInstruction : Instruction { }

public class LogLine : NonInterruptingInstruction
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

    [JsonProperty("checkpoint_request", NullValueHandling = NullValueHandling.Ignore)]
    public CheckpointRequestPayload? CheckpointRequest { get; set; } = null!;
}

public class UpdateSyncStatus : NonInterruptingInstruction
{
    [JsonProperty("status")]
    public CoreSyncStatus Status { get; set; } = null!;
}


public class StreamSubscriptionProgress
{
    [JsonProperty("total")]
    public int Total { get; set; }

    [JsonProperty("downloaded")]
    public int Downloaded { get; set; }
}

/// <summary>
/// An `ActiveStreamSubscription` from the core extension + serialized progress information.
/// </summary>
public class CoreStreamSubscription
{
    [JsonProperty("progress")]
    public StreamSubscriptionProgress Progress { get; set; } = null!;

    [JsonProperty("name")]
    public string Name { get; set; } = null!;

    [JsonProperty("parameters")]
    public Dictionary<string, object> Parameters { get; set; } = null!;

    [JsonProperty("priority")]
    public int? Priority { get; set; }

    [JsonProperty("active")]
    public bool Active { get; set; }

    [JsonProperty("is_default")]
    public bool IsDefault { get; set; }

    [JsonProperty("has_explicit_subscription")]
    public bool HasExplicitSubscription { get; set; }

    [JsonProperty("expires_at")]
    public long? ExpiresAt { get; set; }

    [JsonProperty("last_synced_at")]
    public long? LastSyncedAt { get; set; }
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

    [JsonProperty("streams")]
    public List<CoreStreamSubscription> Streams { get; set; } = [];

    [JsonProperty("internal_last_applied_checkpoint_request_id")]
    public long? LastAppliedCheckpointRequestId { get; set; }
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

public class FetchCredentials : NonInterruptingInstruction
{
    [JsonProperty("did_expire")]
    public bool DidExpire { get; set; }
}

public class CloseSyncStream : Instruction
{
    [JsonProperty("hide_disconnect")]
    public bool HideDisconnect { get; set; }
}

public class DidCompleteSync : NonInterruptingInstruction { }

public class UnknownSyncInstruction(JObject source) : NonInterruptingInstruction
{
    public JObject Source { get; } = source;
}

public class CoreInstructionHelpers
{
    /// <summary>
    /// Decodes a core extension timestamp.
    /// </summary>
    public static DateTime? FromCoreTimestamp(long? microseconds)
    {
        // 1 microsecond = 10 ticks.
        return microseconds.HasValue
            ? UnixEpochUtc.AddTicks(microseconds.Value * 10)
            : null;
    }

    private static readonly DateTime UnixEpochUtc = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static DB.Crud.SyncPriorityStatus PriorityToStatus(SyncPriorityStatus status)
    {
        return new DB.Crud.SyncPriorityStatus
        {
            Priority = status.Priority,
            HasSynced = status.HasSynced,
            LastSyncedAt = FromCoreTimestamp(status.LastSyncedAt)
        };
    }

    public static DB.Crud.SyncStatus CoreStatusToSyncStatus(CoreSyncStatus status)
    {
        return new DB.Crud.SyncStatus(CoreStatusToSyncStatusOptions(status));
    }

    public static DB.Crud.SyncStatusOptions CoreStatusToSyncStatusOptions(CoreSyncStatus status)
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
                DownloadProgress = status.Downloading?.Buckets,
                InternalStreamSubscriptions = status.Streams.ToArray()
            },
            LastSyncedAt = completeSync?.LastSyncedAt,
            HasSynced = completeSync?.HasSynced,
            PriorityStatusEntries = status.PriorityStatus.Select(PriorityToStatus).ToArray()
        };
    }
}
