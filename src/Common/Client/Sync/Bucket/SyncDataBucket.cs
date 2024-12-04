namespace Common.Client.Sync.Bucket;

using System.Text.Json.Serialization;

public class SyncDataBucketJSON
{
    [JsonPropertyName("bucket")]
    public string Bucket { get; set; } = null!;

    [JsonPropertyName("has_more")]
    public bool? HasMore { get; set; }

    [JsonPropertyName("after")]
    public string? After { get; set; }

    [JsonPropertyName("next_after")]
    public string? NextAfter { get; set; }

    [JsonPropertyName("data")]
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
            Data = Data.Select(entry => entry.ToJSON()).ToList()
        };
    }
}