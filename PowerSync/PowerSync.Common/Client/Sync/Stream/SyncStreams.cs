namespace PowerSync.Common.Client.Sync.Stream;

/// <summary>
/// A description of a sync stream, consisting of its <see cref="Name"/> and the <see cref="Parameters"/> used when subscribing.
/// </summary>
public interface ISyncStreamDescription
{
    /// <summary>
    /// The name of the stream as it appears in the stream definition for the PowerSync service.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The parameters used to subscribe to the stream, if any.
    /// The same stream can be subscribed to multiple times with different parameters.
    /// </summary>
    Dictionary<string, object>? Parameters { get; }
}

/// <summary>
/// Information about a subscribed sync stream.
/// This includes the <see cref="ISyncStreamDescription"/>, along with information about the current sync status.
/// </summary>
public class SyncSubscriptionDescription : ISyncStreamDescription
{
    /// <inheritdoc />
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public Dictionary<string, object>? Parameters { get; set; }

    /// <summary>
    /// Whether this stream subscription is currently active.
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// Whether this stream subscription is included by default, regardless of whether the stream has explicitly been
    /// subscribed to or not.
    /// It's possible for both <see cref="IsDefault"/> and <see cref="HasExplicitSubscription"/> to be true at the same time -
    /// this happens when a default stream was subscribed explicitly.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Whether this stream has been subscribed to explicitly.
    /// It's possible for both <see cref="IsDefault"/> and <see cref="HasExplicitSubscription"/> to be true at the same time -
    /// this happens when a default stream was subscribed explicitly.
    /// </summary>
    public bool HasExplicitSubscription { get; set; }

    /// <summary>
    /// For sync streams that have a time-to-live, the current time at which the stream would expire if not subscribed to again.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Whether this stream subscription has been synced at least once.
    /// </summary>
    public bool HasSynced { get; set; }

    /// <summary>
    /// If <see cref="HasSynced"/> is true, the last time data from this stream has been synced.
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }
}

/// <summary>
/// Options for subscribing to a sync stream.
/// </summary>
public class SyncStreamSubscribeOptions
{
    /// <summary>
    /// A "time to live" for this stream subscription, in seconds.
    /// The TTL controls when a stream gets evicted after not having an active <see cref="ISyncStreamSubscription"/> object
    /// attached to it.
    /// </summary>
    public int? Ttl { get; set; }

    /// <summary>
    /// A priority to assign to this subscription. This overrides the default priority that may have been set on streams.
    /// For details on priorities, see prioritized sync documentation.
    /// </summary>
    public SyncPriority? Priority { get; set; }
}

/// <summary>
/// Priority levels for sync stream subscriptions.
/// </summary>
public enum SyncPriority
{
    Priority_0 = 0,
    Priority_1 = 1,
    Priority_2 = 2,
    Priority_3 = 3
}

/// <summary>
/// A handle to a <see cref="ISyncStreamDescription"/> that allows subscribing to the stream.
/// To obtain an instance of <see cref="ISyncStream"/>, call <see cref="PowerSyncDatabase.SyncStream"/>.
/// </summary>
public interface ISyncStream : ISyncStreamDescription
{
    /// <summary>
    /// Adds a subscription to this stream, requesting it to be included when connecting to the sync service.
    /// You should keep a reference to the returned <see cref="ISyncStreamSubscription"/> object as long as you need data for that
    /// stream. As soon as <see cref="ISyncStreamSubscription.Unsubscribe"/> is called for all subscriptions on this stream,
    /// the <see cref="SyncStreamSubscribeOptions.Ttl"/> starts ticking and will
    /// eventually evict the stream (unless <see cref="Subscribe"/> is called again).
    /// </summary>
    Task<ISyncStreamSubscription> Subscribe(SyncStreamSubscribeOptions? options = null);

    /// <summary>
    /// Clears all subscriptions attached to this stream and resets the TTL for the stream.
    /// This is a potentially dangerous operation, as it interferes with other stream subscriptions.
    /// </summary>
    Task UnsubscribeAll();
}

/// <summary>
/// Represents an active subscription to a sync stream.
/// </summary>
public interface ISyncStreamSubscription : ISyncStreamDescription, IDisposable
{
    /// <summary>
    /// Waits until data from this sync stream has been synced and applied.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token to abort the wait.</param>
    /// <returns>A task that completes when the first sync is done.</returns>
    Task WaitForFirstSync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes this stream subscription.
    /// </summary>
    void Unsubscribe();
}
