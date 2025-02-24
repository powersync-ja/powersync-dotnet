namespace Common.DB.Crud;

using Newtonsoft.Json;

public class SyncDataFlowStatus
{
    [JsonProperty("downloading")]
    public bool Downloading { get; set; } = false;

    [JsonProperty("uploading")]
    public bool Uploading { get; set; } = false;
}

public class SyncStatusOptions
{
    public SyncStatusOptions() { }

    public SyncStatusOptions(SyncStatusOptions options)
    {
        Connected = options.Connected;
        Connecting = options.Connecting;
        DataFlow = options.DataFlow;
        LastSyncedAt = options.LastSyncedAt;
        HasSynced = options.HasSynced;
    }


    [JsonProperty("connected")]
    public bool? Connected { get; set; }

    [JsonProperty("connecting")]
    public bool? Connecting { get; set; }

    [JsonProperty("dataFlow")]
    public SyncDataFlowStatus? DataFlow { get; set; }

    [JsonProperty("lastSyncedAt")]
    public DateTime? LastSyncedAt { get; set; }

    [JsonProperty("hasSynced")]
    public bool? HasSynced { get; set; }
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


    public bool IsEqual(SyncStatus status)
    {
        return JsonConvert.SerializeObject(Options) == JsonConvert.SerializeObject(status.Options);
    }

    public string GetMessage()
    {
        var dataFlow = DataFlowStatus;
        return $"SyncStatus<connected: {Connected} connecting: {Connecting} lastSyncedAt: {LastSyncedAt} hasSynced: {HasSynced}. Downloading: {dataFlow.Downloading}. Uploading: {dataFlow.Uploading}>";
    }

    public string ToJSON()
    {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }
}