namespace Common.Client.Sync.Bucket;

public class SyncDataBatch(SyncDataBucket[] buckets)
{
    public SyncDataBucket[] Buckets { get; private set; } = buckets;
}