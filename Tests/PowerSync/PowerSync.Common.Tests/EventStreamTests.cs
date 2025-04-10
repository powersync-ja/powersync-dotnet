namespace PowerSync.Common.Tests;

using PowerSync.Common.Utils;
using PowerSync.Common.DB.Crud;

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
}