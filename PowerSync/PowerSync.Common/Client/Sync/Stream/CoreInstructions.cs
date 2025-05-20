using Newtonsoft.Json.Linq;

namespace PowerSync.Common.Client.Sync.Stream;

using Newtonsoft.Json;
using PowerSync.Common.Client.Sync.Stream;

[JsonConverter(typeof(InstructionConverter))]
public abstract class Instruction
{
}

public class LogLine: Instruction
{
    [JsonProperty("severity")]
    public string Severity { get; set; } = null!;  // "DEBUG", "INFO", "WARNING"

    [JsonProperty("line")]
    public string Line { get; set; } = null!;
}

public class EstablishSyncStream: Instruction
{
    [JsonProperty("request")]
    public StreamingSyncRequest Request { get; set; } = null!;
}

public class UpdateSyncStatus: Instruction
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
    public List<SyncPriorityStatus> PriorityStatus { get; set; } = null!;

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

public class FetchCredentials: Instruction
{
    [JsonProperty("did_expire")]
    public bool DidExpire { get; set; }
}

public class CloseSyncStream : Instruction { }
public class FlushFileSystem : Instruction { }
public class DidCompleteSync : Instruction { }

public class InstructionConverter : JsonConverter<Instruction>
{
    public override Instruction ReadJson(JsonReader reader, Type objectType, Instruction? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var jsonObject = JObject.Load(reader);

        if (jsonObject.ContainsKey("LogLine"))
            return jsonObject["LogLine"]!.ToObject<LogLine>(serializer)!;
        if (jsonObject.ContainsKey("UpdateSyncStatus"))
            return jsonObject["UpdateSyncStatus"]!.ToObject<UpdateSyncStatus>(serializer)!;
        if (jsonObject.ContainsKey("EstablishSyncStream"))
            return jsonObject["EstablishSyncStream"]!.ToObject<EstablishSyncStream>(serializer)!;
        if (jsonObject.ContainsKey("FetchCredentials"))
            return jsonObject["FetchCredentials"]!.ToObject<FetchCredentials>(serializer)!;
        if (jsonObject.ContainsKey("CloseSyncStream"))
            return new CloseSyncStream();
        if (jsonObject.ContainsKey("FlushFileSystem"))
            return new FlushFileSystem();
        if (jsonObject.ContainsKey("DidCompleteSync"))
            return new DidCompleteSync();

        throw new JsonSerializationException("Unknown Instruction type.");
    }

    public override void WriteJson(JsonWriter writer, Instruction? value, JsonSerializer serializer)
    {
        throw new NotImplementedException("Writing not implemented.");
    }
}