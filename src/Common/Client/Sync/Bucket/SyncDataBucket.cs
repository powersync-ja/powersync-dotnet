namespace Common.Client.Sync.Bucket;

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
    public List<OplogEntryJSON> Data { get; set; } = [];
}

public class SyncDataBucket(
    string bucket,
    List<OplogEntry> data,
    bool hasMore,
    string? after = null,
    string? nextAfter = null)
{
    public string Bucket { get; private set; } = bucket;
    public List<OplogEntry> Data { get; private set; } = data;
    public bool HasMore { get; private set; } = hasMore;
    public string? After { get; private set; } = after;
    public string? NextAfter { get; private set; } = nextAfter;

    public static SyncDataBucket FromRow(SyncDataBucketJSON row)
    {
        return new SyncDataBucket(
            row.Bucket,
            row.Data.Select(OplogEntry.FromRow).ToList(),
            row.HasMore ?? false,
            row.After,
            row.NextAfter
        );
    }

    public SyncDataBucketJSON ToJSON()
    {
        return new SyncDataBucketJSON
        {
            Bucket = Bucket,
            HasMore = HasMore,
            After = After,
            NextAfter = NextAfter,
            Data = [.. Data.Select(entry => entry.ToJSON())]
        };
    }
}