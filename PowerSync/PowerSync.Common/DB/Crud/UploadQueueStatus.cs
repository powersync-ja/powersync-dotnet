namespace PowerSync.Common.DB.Crud;

public class UploadQueueStats(long count, long? size = null)
{
    public long Count { get; set; } = count;

    public long? Size { get; set; } = size;

    public override string ToString()
    {
        if (Size == null)
        {
            return $"UploadQueueStats<count: {Count}>";
        }
        else
        {
            return $"UploadQueueStats<count: {Count} size: {Size / 1024.0}kB>";
        }
    }
}
