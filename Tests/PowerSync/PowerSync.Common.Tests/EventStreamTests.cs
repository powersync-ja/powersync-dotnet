namespace PowerSync.Common.Tests;

using PowerSync.Common.DB.Crud;
using PowerSync.Common.Utils;

public class EventStreamTests
{
    [Fact]
    public async Task EventStream_ShouldReceiveTwoMessages_Async()
    {
        var eventStream = new EventStream<SyncStatus>();

        var cts = new CancellationTokenSource();
        var receivedMessages = new List<SyncStatus>();

        var completedTask = new TaskCompletionSource<bool>();
        var listenerReadySource = new TaskCompletionSource<bool>();

        var listenTask = Task.Run(async () =>
        {
            var stream = eventStream.ListenAsync(cts.Token);

            listenerReadySource.TrySetResult(true);

            await foreach (var status in stream)
            {
                receivedMessages.Add(status);

                if (receivedMessages.Count == 2)
                {
                    cts.Cancel();
                }
            }
            completedTask.SetResult(true);
        });

        await listenerReadySource.Task;
        Assert.Equal(1, eventStream.SubscriberCount());

        var status1 = new SyncStatus(new SyncStatusOptions
        {
            Connected = true,
        });

        var status2 = new SyncStatus(new SyncStatusOptions
        {
            Connected = false,
        });

        eventStream.Emit(status1);
        eventStream.Emit(status2);

        await completedTask.Task;

        Assert.Equal(2, receivedMessages.Count);
        Assert.Contains(status1, receivedMessages);
        Assert.Contains(status2, receivedMessages);
        Assert.Equal(0, eventStream.SubscriberCount());
    }

    [Fact]
    public async Task EventStream_ShouldReceiveTwoMessages_Sync()
    {
        var eventStream = new EventStream<SyncStatus>();
        var cts = new CancellationTokenSource();
        var receivedMessages = new List<SyncStatus>();

        var completedTask = new TaskCompletionSource<bool>();
        var listenerReadySource = new TaskCompletionSource<bool>();

        var listenTask = Task.Run(() =>
        {
            var stream = eventStream.Listen(cts.Token);

            listenerReadySource.SetResult(true);

            foreach (var status in stream)
            {
                receivedMessages.Add(status);
                if (receivedMessages.Count == 2)
                {
                    cts.Cancel();
                }
            }
            completedTask.SetResult(true);
        });

        await listenerReadySource.Task;
        Assert.Equal(1, eventStream.SubscriberCount());

        var status1 = new SyncStatus(new SyncStatusOptions
        {
            Connected = true,
        });

        var status2 = new SyncStatus(new SyncStatusOptions
        {
            Connected = false,
        });

        eventStream.Emit(status1);
        eventStream.Emit(status2);

        await completedTask.Task;

        Assert.Equal(2, receivedMessages.Count);
        Assert.Contains(status1, receivedMessages);
        Assert.Contains(status2, receivedMessages);
        Assert.Equal(0, eventStream.SubscriberCount());
    }

    [Fact]
    public void EventManager_RegistersStreamsCorrectly()
    {
        var manager = new EventManager();
        var stream1 = new EventStream<bool>();
        var stream2 = new EventStream<string>();
        var stream3 = new EventStream<int>(); // Control

        manager.Register(stream1);
        manager.Register(stream2);

        Assert.True(manager.TryGetStream<bool>(out var obtainedStream1));
        Assert.True(manager.TryGetStream<string>(out var obtainedStream2));
        Assert.False(manager.TryGetStream<int>(out var obtainedStream3));

        Assert.Equal(stream1, obtainedStream1);
        Assert.Equal(stream2, obtainedStream2);
        Assert.Null(obtainedStream3);

        manager.Close();
    }

    [Fact]
    public void EventManager_CloseRemovesAndClosesStreams()
    {
        var manager = new EventManager();
        var stream1 = new EventStream<bool>();
        var stream2 = new EventStream<string>();
        var stream3 = new EventStream<int>(); // Control

        manager.Register(stream1);
        manager.Register(stream2);

        manager.Close();

        Assert.False(manager.TryGetStream<bool>(out var obtainedStream1));
        Assert.False(manager.TryGetStream<string>(out var obtainedStream2));
        Assert.False(manager.TryGetStream<int>(out var obtainedStream3));

        Assert.Null(obtainedStream1);
        Assert.Null(obtainedStream2);
        Assert.Null(obtainedStream3);

        Assert.True(stream1.Closed);
        Assert.True(stream2.Closed);
        Assert.False(stream3.Closed);
    }

    [Fact(Timeout = 2000)]
    public async Task EventManager_ShouldReceiveEmittedEvents()
    {
        var manager = new EventManager();
        var stream1 = new EventStream<bool>();
        var stream2 = new EventStream<string>();

        manager.Register(stream1);
        manager.Register(stream2);

        var cts1 = new CancellationTokenSource();
        var listener1 = stream1.ListenAsync(cts1.Token);
        Assert.True(manager.TryEmit(false));
        Assert.True(manager.TryEmit(false));
        Assert.True(manager.TryEmit(true));
        int eventCount = 0;

        await foreach (var evt in listener1)
        {
            eventCount++;
            if (evt == true)
            {
                cts1.Cancel();
            }
        }

        Assert.Equal(3, eventCount);

        var cts2 = new CancellationTokenSource();
        var listener2 = stream2.ListenAsync(cts2.Token);
        Assert.True(manager.TryEmit("hi"));
        Assert.True(manager.TryEmit("hello"));
        Assert.True(manager.TryEmit("sup"));
        Assert.True(manager.TryEmit("STOP"));
        eventCount = 0;

        await foreach (var evt in listener2)
        {
            eventCount++;
            if (evt == "STOP")
            {
                cts2.Cancel();
            }
        }

        Assert.Equal(4, eventCount);

        manager.Close();
    }

    [Fact]
    public async Task EventManager_ShouldNotReceiveEventsAfterDeregistering()
    {
        var manager = new EventManager();
        var stream = new EventStream<string>();

        manager.Register(stream);

        var cts = new CancellationTokenSource();
        var listener = stream.ListenAsync(cts.Token);
        var sem = new SemaphoreSlim(0);
        int eventCount = 0;

        _ = Task.Run(async () =>
        {
            sem.Release();
            await foreach (var evt in listener)
            {
                eventCount++;
                sem.Release();
            }
        }, cts.Token);
        Assert.True(await sem.WaitAsync(100));

        Assert.True(manager.Deregister<string>());

        Assert.False(manager.TryEmit("invalid"));
        Assert.False(await sem.WaitAsync(100));

        // Cleanup
        cts.Cancel();
        manager.Close();
    }
}
