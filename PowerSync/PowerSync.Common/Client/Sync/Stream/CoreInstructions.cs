using Newtonsoft.Json.Linq;

namespace PowerSync.Common.Client.Sync.Stream;

using Newtonsoft.Json;

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
        
        Console.WriteLine("Scanning instructions: "+ jsonArray.Count);
        foreach (JObject item in jsonArray)
        {
            instructions.Add(ParseInstruction(item));
            Console.WriteLine("Parsed instruction: " + JsonConvert.SerializeObject(ParseInstruction(item)));
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

public class FetchCredentials: Instruction
{
    [JsonProperty("did_expire")]
    public bool DidExpire { get; set; }
}

public class CloseSyncStream : Instruction { }
public class FlushFileSystem : Instruction { }
public class DidCompleteSync : Instruction { }