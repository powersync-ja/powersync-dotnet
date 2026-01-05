namespace PowerSync.Common.Client.Sync.Bucket;

using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

public class SyncDataBucketJSON
{
    [JsonProperty("bucket")]
    public string Bucket { get; set; } = null!;

    [JsonProperty("has_more")]
    public bool? HasMore { get; set; }

    [JsonProperty("after")]
    public string? After { get; set; }

    [JsonProperty("next_after")]
    public string? NextAfter { get; set; }

    [JsonProperty("data")]
    public List<object> Data { get; set; } = [];
}

public class SyncDataBucket(
    string bucket,
    OplogEntry[] data,
    bool hasMore,
    string? after = null,
    string? nextAfter = null)
{
    public string Bucket { get; private set; } = bucket;
    public OplogEntry[] Data { get; private set; } = data;
    public bool HasMore { get; private set; } = hasMore;
    public string? After { get; private set; } = after;
    public string? NextAfter { get; private set; } = nextAfter;

    public static SyncDataBucket FromRow(SyncDataBucketJSON row)
    {
        var dataEntries = row.Data != null
            ? row.Data
                .Select(obj => JsonConvert.DeserializeObject<OplogEntryJSON>(JsonConvert.SerializeObject(obj))!) // Convert object to JSON string, then deserialize
                .Select(OplogEntry.FromRow)
                .ToArray()
            : [];

        return new SyncDataBucket(
            row.Bucket,
            dataEntries,
            row.HasMore ?? false,
            row.After,
            row.NextAfter
        );
    }

    public string ToJSON()
    {
        List<object> dataObjects = Data
         .Select(entry => JsonConvert.DeserializeObject<object>(entry.ToJSON()))
         .Where(obj => obj != null)
         .ToList()!;

        var jsonObject = new SyncDataBucketJSON
        {
            Bucket = Bucket,
            HasMore = HasMore,
            After = After,
            NextAfter = NextAfter,
            Data = dataObjects
        };

        return JsonConvert.SerializeObject(jsonObject);
    }
}
