namespace PowerSync.Common.Client;

using System.Collections.Concurrent;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PowerSync.Common.DB;

/// <summary>
/// Owns all watched queries for a <see cref="PowerSyncDatabase"/>.
///
/// A single listener on the DB adapter's table-update events dispatches changes to each
/// registered <see cref="WatchSubscription"/>, and a single listener on schema changes marks
/// every subscription's resolved source tables as stale. Subscriptions are otherwise fully
/// independent: each one throttles, re-resolves its source tables and re-runs its query on
/// its own consumer-driven loop, so a slow or failing query only affects its own stream.
/// </summary>
internal class WatchManager
{
    internal const int DEFAULT_THROTTLE_MS = 30;

    private const string PS_DATA_PREFIX = "ps_data__";
    private const string PS_DATA_LOCAL_PREFIX = "ps_data_local__";

    private readonly PowerSyncDatabase db;
    private readonly CancellationToken masterToken;
    private readonly ConcurrentDictionary<WatchSubscription, byte> watches = new();

    public WatchManager(PowerSyncDatabase db, CancellationToken masterToken)
    {
        this.db = db;
        this.masterToken = masterToken;

        // Dispatch two tasks, one for TableUpdated events and one for SchemaChanged events
        _ = Task.Run(RunTableUpdateLoop);
        _ = Task.Run(RunSchemaChangeLoop);
    }

    public IAsyncEnumerable<T[]> Watch<T>(string sql, object?[]? parameters, SQLWatchOptions? options)
    {
        options ??= new SQLWatchOptions();

        // Register synchronously so that table changes between this call and the consumer
        // starting iteration are not missed.
        var subscription = CreateSubscription(
            options,
            resolveTables: options.Tables != null
                ? () => Task.FromResult(ExpandTableNames(options.Tables))
                : () => db.GetSourceTables(sql, parameters),
            initialTables: options.Tables != null ? ExpandTableNames(options.Tables) : null,
            // Don't flush updates mid-cancellation, since that may involve expensive
            // database operations.
            flushOnCancel: false,
            refreshOnSchemaChange: true
        );

        return Stream(subscription, _ => db.GetAll<T>(sql, parameters));
    }

    public IAsyncEnumerable<WatchOnChangeEvent> OnChange(SQLWatchOptions? options)
    {
        options ??= new SQLWatchOptions();

        var tables = ExpandTableNames(options.Tables ?? []);
        var subscription = CreateSubscription(
            options,
            resolveTables: () => Task.FromResult(new HashSet<string>(tables)),
            initialTables: tables,
            // Deliver changes accumulated during the throttle window even if cancellation
            // lands before the window expires.
            flushOnCancel: true,
            refreshOnSchemaChange: false
        );

        // TODO: powersync-js onChange returns table names in `ps_data__{table}` format.
        //       We should make a decision on whether or not to mirror that before v1.
        return Stream(subscription, changed => Task.FromResult(new WatchOnChangeEvent
        {
            ChangedTables = [.. changed.Select(InternalToFriendlyTableName)]
        }));
    }

    private WatchSubscription CreateSubscription(
        SQLWatchOptions options,
        Func<Task<HashSet<string>>> resolveTables,
        HashSet<string>? initialTables,
        bool flushOnCancel,
        bool refreshOnSchemaChange
    )
    {
        var cts = options.Signal != null
            ? CancellationTokenSource.CreateLinkedTokenSource(masterToken, options.Signal.Value)
            : CancellationTokenSource.CreateLinkedTokenSource(masterToken);

        var subscription = new WatchSubscription(
            resolveTables,
            initialTables,
            options.ThrottleMs ?? DEFAULT_THROTTLE_MS,
            options.TriggerImmediately,
            flushOnCancel,
            refreshOnSchemaChange,
            cts
        );

        watches.TryAdd(subscription, 0);

        // Cleanup must not depend on the consumer ever iterating the stream: on any cancellation
        // (external Signal, master token, or Unregister) drop the subscription from the registry.
        // CTS disposal stays in Unregister - disposing from inside a cancellation callback is unsafe.
        cts.Token.Register(() => watches.TryRemove(subscription, out _));

        return subscription;
    }

    private void Unregister(WatchSubscription subscription)
    {
        if (!subscription.TryClose()) return;

        watches.TryRemove(subscription, out _);
        subscription.Cts.Cancel();
        subscription.Cts.Dispose();
    }

    private async IAsyncEnumerable<T> Stream<T>(WatchSubscription subscription, Func<HashSet<string>, Task<T>> onChange)
    {
        try
        {
            while (true)
            {
                var changed = await subscription.WaitForChangeAsync();
                if (changed == null) yield break;

                yield return await onChange(changed);
            }
        }
        finally
        {
            Unregister(subscription);
        }
    }

    private async Task RunTableUpdateLoop()
    {
        try
        {
            await foreach (var update in db.Database.Events.OnTablesUpdated.ListenAsync(masterToken))
            {
                // Prevent a single bad notification from taking down the entire TablesUpdated loop
                try
                {
                    var changed = new HashSet<string>(DBAdapterUtils.ExtractTableUpdates(update.TablesUpdated));
                    if (changed.Count == 0) continue;

                    foreach (var entry in watches)
                    {
                        entry.Key.NotifyTablesChanged(changed);
                    }
                }
                catch (Exception ex)
                {
                    db.Logger.LogError(ex, "Failed to dispatch a table update to watched queries.");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            db.Logger.LogError(ex, "Watch OnTablesUpdated dispatcher failed; terminating all watched queries.");
        }
        finally
        {
            foreach (var entry in watches)
            {
                watches.TryRemove(entry.Key, out _);
                entry.Key.Terminate();
            }
        }
    }

    private async Task RunSchemaChangeLoop()
    {
        try
        {
            await foreach (var _ in db.Events.OnSchemaChanged.ListenAsync(masterToken))
            {
                foreach (var entry in watches)
                {
                    entry.Key.RequestRefresh();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            db.Logger.LogError(ex, "Watch OnSchemaChanged dispatcher failed; watched queries will not react to schema changes.");
        }
    }

    private static HashSet<string> ExpandTableNames(IEnumerable<string> tables) =>
        [.. tables.SelectMany(table => new[] { $"{PS_DATA_PREFIX}{table}", $"{PS_DATA_LOCAL_PREFIX}{table}" })];

    private static string InternalToFriendlyTableName(string internalName)
    {
        if (internalName.StartsWith(PS_DATA_PREFIX))
            return internalName.Substring(PS_DATA_PREFIX.Length);

        if (internalName.StartsWith(PS_DATA_LOCAL_PREFIX))
            return internalName.Substring(PS_DATA_LOCAL_PREFIX.Length);

        return internalName;
    }
}

/// <summary>
/// Dispatch state for a single watched query.
///
/// The manager pushes changed table names in via <see cref="NotifyTablesChanged"/> and schema
/// resets via <see cref="RequestRefresh"/>; the consumer pulls coalesced change batches out via
/// <see cref="WaitForChangeAsync"/>. Throttling is per-subscription: the signal channel has a
/// capacity of one, so any number of notifications during the throttle window or an in-flight
/// query collapse into a single pending wake-up.
/// </summary>
internal class WatchSubscription
{
    private readonly object mutex = new();
    private readonly Channel<bool> signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }
    );

    private readonly Func<Task<HashSet<string>>> resolveTables;
    private readonly int throttleMs;
    private readonly bool flushOnCancel;
    private readonly bool refreshOnSchemaChange;
    // Static table sets (explicit SQLWatchOptions.Tables) survive schema refreshes: only the
    // underlying data changes, never the expansion, so a refresh re-runs the query without
    // discarding the set.
    private readonly bool staticTables;

    private int closed;

    // The resolved source tables of the query. Null means "not resolved" - either not yet
    // resolved, or invalidated by a schema change. While null, all table notifications are
    // accumulated unfiltered and filtered against the newly resolved set later, so no
    // relevant change is missed during (re)resolution.
    private HashSet<string>? tables;
    private HashSet<string> pending = [];
    private bool refreshRequested;

    internal CancellationTokenSource Cts { get; }

    public WatchSubscription(
        Func<Task<HashSet<string>>> resolveTables,
        HashSet<string>? initialTables,
        int throttleMs,
        bool triggerImmediately,
        bool flushOnCancel,
        bool refreshOnSchemaChange,
        CancellationTokenSource cts
    )
    {
        this.resolveTables = resolveTables;
        this.throttleMs = throttleMs;
        this.flushOnCancel = flushOnCancel;
        this.refreshOnSchemaChange = refreshOnSchemaChange;
        staticTables = initialTables != null;
        tables = initialTables;
        Cts = cts;

        if (triggerImmediately)
        {
            refreshRequested = true;
            signal.Writer.TryWrite(true);
        }
    }

    public void NotifyTablesChanged(HashSet<string> changed)
    {
        lock (mutex)
        {
            if (tables == null)
            {
                pending.UnionWith(changed);
            }
            else
            {
                var relevant = false;
                foreach (var table in changed)
                {
                    if (tables.Contains(table))
                    {
                        pending.Add(table);
                        relevant = true;
                    }
                }
                if (!relevant) return;
            }
        }
        signal.Writer.TryWrite(true);
    }

    public void RequestRefresh()
    {
        lock (mutex)
        {
            if (!refreshOnSchemaChange) return;

            refreshRequested = true;
            if (!staticTables) tables = null;
        }
        signal.Writer.TryWrite(true);
    }

    /// <summary>
    /// Ends the subscription's stream gracefully without cancelling it: any pending batch is
    /// still delivered, then <see cref="WaitForChangeAsync"/> returns null. Used when the
    /// dispatcher can no longer deliver updates.
    /// </summary>
    public void Terminate()
    {
        signal.Writer.TryComplete();
    }

    /// <summary>
    /// Marks the subscription closed. Returns false if it was already closed, making
    /// unregistration idempotent across multiple enumerations of the same stream.
    /// </summary>
    public bool TryClose()
    {
        return Interlocked.Exchange(ref closed, 1) == 0;
    }

    /// <summary>
    /// Waits for the next batch of relevant table changes, applying the throttle window.
    /// Returns the changed table names (empty for refresh/immediate triggers, where the query
    /// must run regardless of which tables changed), or null when the subscription ends.
    /// </summary>
    public async Task<HashSet<string>?> WaitForChangeAsync()
    {
        while (true)
        {
            try
            {
                if (!await signal.Reader.WaitToReadAsync(Cts.Token)) return null;
            }
            catch (OperationCanceledException)
            {
                return FlushPendingOnCancel();
            }
            catch (ObjectDisposedException)
            {
                // A prior enumeration of the same stream already unregistered this subscription
                return null;
            }

            bool refresh;
            lock (mutex) refresh = refreshRequested;

            // Refresh and TriggerImmediately wake-ups bypass the throttle so that consumers see
            // post-schema-change results (and initial results) without delay.
            if (!refresh && throttleMs > 0)
            {
                try
                {
                    await Task.Delay(throttleMs, Cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return FlushPendingOnCancel();
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }

            // Consume the signal only after the throttle window, so notifications that arrived
            // during the window coalesce into this batch instead of causing another wake-up.
            signal.Reader.TryRead(out _);

            HashSet<string> batch;
            HashSet<string>? currentTables;
            lock (mutex)
            {
                refresh = refreshRequested;
                refreshRequested = false;
                batch = pending;
                pending = [];
                currentTables = tables;
            }

            if (currentTables == null)
            {
                currentTables = await resolveTables();
                lock (mutex)
                {
                    // A concurrent RequestRefresh may have nulled `tables` again; don't clobber it,
                    // the next wake-up will re-resolve.
                    if (!refreshRequested) tables = currentTables;
                }
            }

            // Names accumulated while unresolved are unfiltered; restrict them to the actual
            // source tables. Already-filtered names are unaffected.
            batch.IntersectWith(currentTables);

            if (refresh) return batch;
            if (batch.Count == 0) continue;
            return batch;
        }
    }

    private HashSet<string>? FlushPendingOnCancel()
    {
        if (!flushOnCancel) return null;

        lock (mutex)
        {
            if (tables == null) return null;

            pending.IntersectWith(tables);
            if (pending.Count == 0) return null;

            var batch = pending;
            pending = [];
            return batch;
        }
    }
}
