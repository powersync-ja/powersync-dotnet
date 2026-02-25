namespace PowerSync.Common.Tests.Client.Sync;

using System.Runtime.CompilerServices;

using Common.Client.Sync.Stream;

using Newtonsoft.Json;

using PowerSync.Common.Client;
using PowerSync.Common.DB.Crud;
using PowerSync.Common.DB.Schema;
using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Tests.Utils.Sync;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "SyncStreamsTests"
/// </summary>
public class SyncStreamsTests : IAsyncLifetime
{

    MockSyncService syncService = null!;
    PowerSyncDatabase db = null!;

    public async Task InitializeAsync()
    {
        syncService = new MockSyncService();
        db = syncService.CreateDatabase();
        await db.Init();
    }

    public async Task DisposeAsync()
    {
        syncService.Close();
        await db.Execute("DELETE FROM ps_stream_subscriptions");
        await db.DisconnectAndClear();
        await db.Close();
    }

    [Fact]
    public async Task CanDisableDefaultStreams()
    {
        await db.Connect(new TestConnector(), new PowerSyncConnectionOptions
        {
            IncludeDefaultStreams = false
        });

        TestUtils.DeepEquivalent(new RequestStream { IncludeDefaults = false, Subscriptions = [] }, syncService.Requests[0].Streams);
    }

    [Fact]
    public async Task BasicSubscribeTest()
    {
        var a = await db.SyncStream("a").Subscribe();

        await db.Connect(new TestConnector(), new PowerSyncConnectionOptions());
        Assert.Equal(1, syncService.Requests[0]?.Streams?.Subscriptions.Count);
        Assert.Equal("a", syncService.Requests[0]?.Streams?.Subscriptions[0].Stream);

        a.Unsubscribe();
    }

    [Fact]
    public async Task SubscribesWithStreams()
    {
        var a = await db.SyncStream("stream", new Dictionary<string, object> { { "foo", "a" } }).Subscribe();
        var b = await db.SyncStream("stream", new Dictionary<string, object> { { "foo", "b" } })
                        .Subscribe(new SyncStreamSubscribeOptions { Priority = new StreamPriority(1) });

        await db.Connect(new TestConnector());

        Assert.True(syncService.Requests[0]?.Streams?.IncludeDefaults);
        Assert.Equal(2, syncService.Requests[0]?.Streams?.Subscriptions.Count);
        TestUtils.DeepEquivalent(
            new RequestStreamSubscription
            {
                Stream = "stream",
                Parameters = new Dictionary<string, object> { { "foo", "a" } },
                OverridePriority = null
            },
            syncService.Requests[0]?.Streams?.Subscriptions[0]
        );
        TestUtils.DeepEquivalent(
            new RequestStreamSubscription
            {
                Stream = "stream",
                Parameters = new Dictionary<string, object> { { "foo", "b" } },
                OverridePriority = 1
            },
            syncService.Requests[0]?.Streams?.Subscriptions[1]
        );

        var statusTask = MockSyncService.NextStatus(db);

        syncService.PushLine(
            MockDataFactory.Checkpoint(lastOpId: 0, buckets: [
                MockDataFactory.Bucket("a", 0, priority: 3, subscriptions: new object[] { new { sub = 0 } }),
                MockDataFactory.Bucket("b", 0, priority: 1, subscriptions: new object[] { new { sub = 1 } })
            ], streams: [MockDataFactory.Stream("stream", false)])
        );
        var status = await statusTask;

        foreach (var subscription in new[] { a, b })
        {
            var statusForStream = status.ForStream(subscription);
            Assert.True(statusForStream!.Subscription.Active);
            Assert.Null(statusForStream!.Subscription.LastSyncedAt);
            Assert.True(statusForStream!.Subscription.HasExplicitSubscription);
        }
        await Task.Delay(100);
        statusTask = MockSyncService.NextStatus(db);

        syncService.PushLine(
            MockDataFactory.CheckpointPartiallyComplete(lastOpId: "0", priority: 1)
        );

        status = await statusTask;

        Assert.Null(status.ForStream(a)!.Subscription.LastSyncedAt);
        Assert.NotNull(status.ForStream(b)!.Subscription.LastSyncedAt);
        await b.WaitForFirstSync();

        syncService.PushLine(MockDataFactory.CheckpointComplete(lastOpId: "0"));
        await a.WaitForFirstSync();
    }

    [Fact]
    public async Task ReportsDefaultStreams()
    {
        await db.Connect(new TestConnector());
        var statusTask = MockSyncService.NextStatus(db);

        syncService.PushLine(
            MockDataFactory.Checkpoint(lastOpId: 0, buckets: [], streams: [MockDataFactory.Stream("default_stream", true)])
        );

        var status = await statusTask;
        var statusSubscription = status.SyncStreams?[0];

        Assert.NotNull(statusSubscription);
        Assert.Equal("default_stream", statusSubscription!.Subscription.Name);
        Assert.Null(statusSubscription!.Subscription.Parameters);
        Assert.True(statusSubscription!.Subscription.IsDefault);
        Assert.False(statusSubscription!.Subscription.HasExplicitSubscription);
    }

    [Fact]
    public async Task ChangesSubscriptionsDynamically()
    {
        await db.Connect(new TestConnector());
        var statusTask = MockSyncService.NextStatus(db);

        syncService.PushLine(
            MockDataFactory.Checkpoint(
                lastOpId: 0,
                buckets: []
            )
        );

        await Task.Delay(100);
        var subscription = await db.SyncStream("a").Subscribe();

        await TestUtils.WaitForAsync(() => syncService.Requests.Count > 1);
        Assert.Single(syncService.Requests[1]?.Streams?.Subscriptions!);

        // Given that the subscription has a TTL, dropping the handle should not re-subscribe.
        subscription.Unsubscribe();
        await Task.Delay(100);
        Assert.Equal(2, syncService.Requests.Count);
    }

    [Fact]
    public async Task SubscriptionsUpdateWhileOfflineTest()
    {
        var statusTask = MockSyncService.NextStatus(db);
        var subscription = await db.SyncStream("foo").Subscribe();
        var status = await statusTask;

        Assert.NotNull(status.ForStream(subscription));
    }

    // FIx
    [Fact]
    public async Task UnsubscribeMultipleTimesHasNoEffectTest()
    {
        var a = await db.SyncStream("a").Subscribe();
        var aAgain = await db.SyncStream("a").Subscribe();
        a.Unsubscribe();
        a.Unsubscribe();

        // Pretend the streams are expired - they should still be requested because the core extension extends the lifetime
        // of streams currently referenced before connecting.
        await db.Execute("UPDATE ps_stream_subscriptions SET expires_at = unixepoch() - 1000");
        await db.Connect(new TestConnector());

        Assert.True(syncService.Requests[0]?.Streams?.IncludeDefaults);
        Assert.Single(syncService.Requests[0]?.Streams?.Subscriptions!);

        aAgain.Unsubscribe();
    }

    [Fact]
    public async Task UnsubscribeAllTest()
    {
        var a = await db.SyncStream("a").Subscribe();
        await db.SyncStream("a").UnsubscribeAll();

        await db.Connect(new TestConnector(), new PowerSyncConnectionOptions());
        TestUtils.DeepEquivalent(new RequestStream { IncludeDefaults = true, Subscriptions = [] }, syncService.Requests[0].Streams);
    }


}
