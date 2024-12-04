namespace Common.Client.Sync.Bucket;

using System;
using System.Collections.Generic;

public class SyncDataBatch(List<SyncDataBucket> buckets)
{
    public List<SyncDataBucket> Buckets { get; private set; } = buckets ?? throw new ArgumentNullException(nameof(buckets));
}