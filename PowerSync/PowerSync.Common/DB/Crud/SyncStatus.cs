namespace PowerSync.Common.DB.Crud;

using PowerSync.Common.Client.Sync.Stream;
using Newtonsoft.Json;
using Microsoft.Extensions.Options;

public class SyncDataFlowStatus
{
    [JsonProperty("downloading")] public bool Downloading { get; set; } = false;

    [JsonProperty("uploading")] public bool Uploading { get; set; } = false;

    /// <summary>
    /// Error during downloading (including connecting).
    /// Cleared on the next successful data download.
    /// </summary>
    [JsonIgnore]
    public Exception? DownloadError { get; set; } = null;

    /// <summary>
    /// Error during uploading.
    /// Cleared on the next successful upload.
    /// </summary>
    [JsonIgnore]
    public Exception? UploadError { get; set; } = null;


    /// <summary>
    /// Internal information about how far we are downloading operations in buckets.
    /// </summary>
    public Dictionary<string, BucketProgress>? DownloadProgress { get; set; } = null;
}

public class SyncPriorityStatus
{
    [JsonProperty("priority")] public int Priority { get; set; }

    [JsonProperty("lastSyncedAt")] public DateTime? LastSyncedAt { get; set; }

    [JsonProperty("hasSynced")] public bool? HasSynced { get; set; }
}

public class SyncStatusOptions
{
    public SyncStatusOptions()
    {
    }

    public SyncStatusOptions(SyncStatusOptions options)
    {
        Connected = options.Connected;
        Connecting = options.Connecting;
        DataFlow = options.DataFlow;
        LastSyncedAt = options.LastSyncedAt;
        HasSynced = options.HasSynced;
        PriorityStatusEntries = options.PriorityStatusEntries;
    }

    [JsonProperty("connected")] public bool? Connected { get; set; }

    [JsonProperty("connecting")] public bool? Connecting { get; set; }

    [JsonProperty("dataFlow")] public SyncDataFlowStatus? DataFlow { get; set; }

    [JsonProperty("lastSyncedAt")] public DateTime? LastSyncedAt { get; set; }

    [JsonProperty("hasSynced")] public bool? HasSynced { get; set; }

    [JsonProperty("priorityStatusEntries")]
    public SyncPriorityStatus[]? PriorityStatusEntries { get; set; }
}

public class SyncStatus(SyncStatusOptions options)
{
    public SyncStatusOptions Options { get; } = options ?? new SyncStatusOptions();

    public bool Connected => Options.Connected ?? false;

    public bool Connecting => Options.Connecting ?? false;

    /// <summary>
    /// Time that the last sync has fully completed, if any.
    /// Currently, this is reset to null after a restart.
    /// </summary>
    public DateTime? LastSyncedAt => Options.LastSyncedAt;

    /// <summary>
    /// Indicates whether there has been at least one full sync.
    /// Is null when unknown, for example when state is still being loaded from the database.
    /// </summary>
    public bool? HasSynced => Options.HasSynced;

    /// <summary>
    /// Upload/download status.
    /// </summary>
    public SyncDataFlowStatus DataFlowStatus => Options.DataFlow ?? new SyncDataFlowStatus();

    /// <summary>
    /// Provides sync status information for all bucket priorities, sorted by priority (highest first).
    /// </summary>
    public SyncPriorityStatus[] PriorityStatusEntries =>
        (Options.PriorityStatusEntries ?? [])
        .OrderBy(x => x, Comparer<SyncPriorityStatus>.Create(ComparePriorities))
        .ToArray();

    /// <summary>
    /// A realtime progress report on how many operations have been downloaded and
    /// how many are necessary in total to complete the next sync iteration.
    /// </summary>
    public SyncProgress? DownloadProgress()
    {
        var internalProgress = Options.DataFlow?.DownloadProgress;
        if (internalProgress == null)
        {
            return null;
        }

        return new SyncProgress(internalProgress);
    }

    /// <summary>
    /// Reports the sync status (a pair of HasSynced and LastSyncedAt fields)
    /// for a specific bucket priority level.
    ///
    /// When buckets with different priorities are declared, PowerSync may choose to synchronize higher-priority
    /// buckets first. When a consistent view over all buckets for all priorities up until the given priority is
    /// reached, PowerSync makes data from those buckets available before lower-priority buckets have finished
    /// syncing.
    ///
    /// This method returns the status for the requested priority or the next higher priority level that has
    /// status information available. This is because when PowerSync makes data for a given priority available,
    /// all buckets in higher-priorities are guaranteed to be consistent with that checkpoint.
    /// For example, if PowerSync just finished synchronizing buckets in priority level 3, calling this method
    /// with a priority of 1 may return information for priority level 3.
    /// </summary>
    public SyncPriorityStatus StatusForPriority(int priority)
    {
        foreach (var known in PriorityStatusEntries)
        {
            if (known.Priority >= priority)
            {
                return known;
            }
        }

        // Fallback if no matching or higher-priority entry is found
        return new SyncPriorityStatus
        {
            Priority = priority,
            LastSyncedAt = LastSyncedAt,
            HasSynced = HasSynced
        };
    }

    /// <summary>
    /// Creates an updated SyncStatus by merging the current status with the provided updated status.
    /// </summary>
    public SyncStatus CreateUpdatedStatus(SyncStatus updatedStatus)
    {
        var updatedOptions = updatedStatus.Options;
        var currentOptions = Options;

        var parsedOptions = new SyncStatusOptions
        {
            Connected = updatedOptions.Connected ?? currentOptions.Connected,
            Connecting = updatedOptions.Connecting ?? currentOptions.Connecting,
            LastSyncedAt = updatedOptions.LastSyncedAt ?? currentOptions.LastSyncedAt,
            HasSynced = updatedOptions.HasSynced ?? currentOptions.HasSynced,
            PriorityStatusEntries = updatedOptions.PriorityStatusEntries ?? currentOptions.PriorityStatusEntries,
            DataFlow = updatedOptions.DataFlow ?? currentOptions.DataFlow,
        };

        return new SyncStatus(parsedOptions);
    }

    private string SerializeObject()
    {
        return JsonConvert.SerializeObject(new { Options, UploadErrorMessage = Options.DataFlow?.UploadError?.Message, DownloadErrorMessage = DataFlowStatus.DownloadError?.Message });
    }

    public bool IsEqual(SyncStatus status)
    {
        return this.SerializeObject() == status.SerializeObject();
    }

    public string GetMessage()
    {
        var dataFlow = DataFlowStatus;
        return
            $"SyncStatus<connected: {Connected} connecting: {Connecting} lastSyncedAt: {LastSyncedAt} hasSynced: {HasSynced}. Downloading: {dataFlow.Downloading}. Uploading: {dataFlow.Uploading}. UploadError: ${dataFlow.UploadError}, DownloadError?: ${dataFlow.DownloadError}>";
    }

    public string ToJSON()
    {
        return SerializeObject();
    }

    private static int ComparePriorities(SyncPriorityStatus a, SyncPriorityStatus b)
    {
        return b.Priority - a.Priority; // Reverse because higher priorities have lower numbers
    }
}