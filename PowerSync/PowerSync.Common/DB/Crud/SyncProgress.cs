using PowerSync.Common.Client.Sync.Stream;

namespace PowerSync.Common.DB.Crud;

/// <summary>
/// Provides realtime progress on how PowerSync is downloading rows.
///
/// The reported progress always reflects the status towards th end of a sync iteration (after
/// which a consistent snapshot of all buckets is available locally).
///
/// In rare cases (in particular, when a [compacting](https://docs.powersync.com/usage/lifecycle-maintenance/compacting-buckets)
/// operation takes place between syncs), it's possible for the returned numbers to be slightly
/// inaccurate. For this reason, the sync progress should be seen as an approximation of progress.
/// The information returned is good enough to build progress bars, but not exact enough to track
/// individual download counts.
///
/// Also note that data is downloaded in bulk, which means that individual counters are unlikely
/// to be updated one-by-one.
/// </summary>
public class SyncProgress : ProgressWithOperations
{
    public static readonly int FULL_SYNC_PRIORITY = 2147483647;
    protected Dictionary<string, BucketProgress> InternalProgress { get; }

    public SyncProgress(Dictionary<string, BucketProgress> progress)
    {
        this.InternalProgress = progress;
        var untilCompletion = UntilPriority(FULL_SYNC_PRIORITY);

        TotalOperations = untilCompletion.TotalOperations;
        DownloadedOperations = untilCompletion.DownloadedOperations;
        DownloadedFraction = untilCompletion.DownloadedFraction;
    }

    public ProgressWithOperations UntilPriority(int priority)
    {
        var total = 0;
        var downloaded = 0;

        foreach (var progress in InternalProgress.Values)
        {
            // Include higher-priority buckets, which are represented by lower numbers.
            if (progress.Priority <= priority)
            {
                downloaded += progress.SinceLast;
                total += progress.TargetCount - progress.AtLast;
            }
        }

        return new ProgressWithOperations
        {
            TotalOperations = total,
            DownloadedOperations = downloaded,
            DownloadedFraction = total == 0 ? 1.0 : (double)downloaded / total
        };
    }
}

/// <summary>
/// Information about a progressing download made by the PowerSync SDK.
/// 
/// </summary>
public class ProgressWithOperations
{
    /// <summary>
    /// The total number of operations to download for the current sync iteration to complete.
    /// </summary>
    public int TotalOperations { get; set; }

    /// <summary>
    /// The number of operations that have already been downloaded.
    /// </summary>
    public int DownloadedOperations { get; set; }

    /// <summary>
    /// This will be a number between 0.0 and 1.0 (inclusive).
    /// 
    /// When this number reaches 1.0, all changes have been received from the sync service.
    /// </summary>
    public double DownloadedFraction { get; set; }
}
