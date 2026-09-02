namespace PowerSync.Common.Client.Sync;

/// <summary>An exception related to checkpoint requests.</summary>
public class CheckpointRequestException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CheckpointRequestException" /> class.</summary>
    public CheckpointRequestException() : base() { }

    /// <summary>Initializes a new instance of the <see cref="CheckpointRequestException" /> class with a specified error message.</summary>
    public CheckpointRequestException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="CheckpointRequestException" /> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
    public CheckpointRequestException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>The connected PowerSync Service does not support checkpoint requests.</summary>
    public static readonly string InstanceNotSupported = "The PowerSync service does not support checkpoint requests. Update to PowerSync service version 1.24.0 or later to use this API.";

    /// <summary>The sync client is disconnected.</summary>
    public static readonly string Disconnected = "Cannot request checkpoints, sync client is disconnected";

    /// <summary>Checkpoint requests are disabled; legacy write checkpoints are enabled.</summary>
    public static readonly string Disabled = "Connected with legacy checkpoint mode, cannot request checkpoints";
}
