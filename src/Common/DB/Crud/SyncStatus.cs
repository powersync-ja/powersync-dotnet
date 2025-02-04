namespace Common.DB.Crud;

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class SyncDataFlowStatus
{
    [JsonProperty("downloading")]
    public bool Downloading { get; set; }

    [JsonProperty("uploading")]
    public bool Uploading { get; set; }

    public SyncDataFlowStatus(bool downloading = false, bool uploading = false)
    {
        Downloading = downloading;
        Uploading = uploading;
    }
}

public class SyncStatusOptions
{
    [JsonProperty("connected")]
    public bool? Connected { get; set; }

    [JsonProperty("dataFlow")]
    public SyncDataFlowStatus? DataFlow { get; set; }

    [JsonProperty("lastSyncedAt")]
    public DateTime? LastSyncedAt { get; set; }

    [JsonProperty("hasSynced")]
    public bool? HasSynced { get; set; }

    public SyncStatusOptions(
        bool? connected = null,
        SyncDataFlowStatus? dataFlow = null,
        DateTime? lastSyncedAt = null,
        bool? hasSynced = null)
    {
        Connected = connected;
        DataFlow = dataFlow;
        LastSyncedAt = lastSyncedAt;
        HasSynced = hasSynced;
    }
}

public class SyncStatus
{
    private readonly SyncStatusOptions _options;

    public SyncStatus(SyncStatusOptions options)
    {
        _options = options;
    }

    public bool Connected => _options.Connected ?? false;

    public DateTime? LastSyncedAt => _options.LastSyncedAt;

    public bool? HasSynced => _options.HasSynced;

    public SyncDataFlowStatus DataFlowStatus => _options.DataFlow ?? new SyncDataFlowStatus();

    public bool IsEqual(SyncStatus status)
    {
        return JToken.DeepEquals(
            JToken.FromObject(_options),
            JToken.FromObject(status._options)
        );
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