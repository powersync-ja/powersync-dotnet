namespace PowerSync.Common.Client.Sync.Bucket;

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public class SyncDataBucketJSON
{
    [JsonProperty("bucket")]
    public string Bucket { get; set; } = null!;

    // [JsonProperty("has_more")]
    // public bool? HasMore { get; set; }

    // [JsonProperty("after")]
    // public string? After { get; set; }

    // [JsonProperty("next_after")]
    // public string? NextAfter { get; set; }

    [JsonProperty("data")]
    public List<object> Data { get; set; } = [];
}

public class SyncDataBucket(
    string bucket,
    OplogEntry[] data
    )
{
    public string Bucket { get; private set; } = bucket;
    public OplogEntry[] Data { get; private set; } = data;

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
            dataEntries
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
            Data = dataObjects
        };

        return JsonConvert.SerializeObject(jsonObject);
    }
}