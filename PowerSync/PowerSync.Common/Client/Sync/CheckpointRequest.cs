namespace PowerSync.Common.Client.Sync;

/// <summary>An exception related to checkpoint requests.</summary>
public class CheckpointRequestException : Exception
{
    private CheckpointRequestException(string message) : base(message) { }

    /// <summary>"The PowerSync service does not support checkpoint requests. Update to PowerSync service version 1.24.0 or later to use this API."</summary>
    internal static readonly CheckpointRequestException InstanceNotSupported = new(
        "The PowerSync service does not support checkpoint requests. Update to PowerSync service version 1.24.0 or later to use this API."
    );

    /// <summary>"Cannot request checkpoints, sync client is disconnected"</summary>
    internal static readonly CheckpointRequestException Disconnected = new(
        "Cannot request checkpoints, sync client is disconnected"
    );

    /// <summary>"Connected with legacy checkpoint mode, cannot request checkpoints"</summary>
    internal static readonly CheckpointRequestException Disabled = new(
        "Connected with legacy checkpoint mode, cannot request checkpoints"
    );
}
