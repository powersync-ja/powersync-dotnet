namespace Common.DB.Crud;

using System.Text.Json;


public class SyncDataFlowStatus(bool downloading = false, bool uploading = false)
{
    public bool Downloading { get; set; } = downloading;
    public bool Uploading { get; set; } = uploading;
}

public class SyncStatusOptions(
        bool? connected = null,
        SyncDataFlowStatus? dataFlow = null,
        DateTime? lastSyncedAt = null,
        bool? hasSynced = null)
{
    public bool? Connected { get; set; } = connected;
    public SyncDataFlowStatus? DataFlow { get; set; } = dataFlow;
    public DateTime? LastSyncedAt { get; set; } = lastSyncedAt;
    public bool? HasSynced { get; set; } = hasSynced;
}

public class SyncStatus(SyncStatusOptions options)
{
    protected SyncStatusOptions options = options;

    public bool Connected => options.Connected ?? false;

    public DateTime? LastSyncedAt => options.LastSyncedAt;

    public bool? HasSynced => options.HasSynced;

    public SyncDataFlowStatus DataFlowStatus => options.DataFlow ?? new SyncDataFlowStatus();

    public bool IsEqual(SyncStatus status)
    {
        return JsonSerializer.Serialize(options) == JsonSerializer.Serialize(status.options);
    }

    public string GetMessage()
    {
        return $"SyncStatus<connected: {Connected}, lastSyncedAt: {LastSyncedAt}, hasSynced: {HasSynced}, " +
                $"downloading: {DataFlowStatus.Downloading}, uploading: {DataFlowStatus.Uploading}>";
    }

    public SyncStatusOptions ToJson()
    {
        return new SyncStatusOptions
        {
            Connected = Connected,
            DataFlow = DataFlowStatus,
            LastSyncedAt = LastSyncedAt,
            HasSynced = HasSynced
        };
    }

    public override string ToString()
    {
        return GetMessage();
    }
}