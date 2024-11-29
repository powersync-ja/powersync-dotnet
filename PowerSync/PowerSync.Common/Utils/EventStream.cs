namespace PowerSync.Common.Utils;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

public interface IEventStream<T>
{
    void Emit(T item);

    Task EmitAsync(T item);

    CancellationTokenSource RunListenerAsync(
    Func<T, Task> callback);

    IAsyncEnumerable<T> ListenAsync(CancellationToken cancellationToken);

    CancellationTokenSource RunListener(Action<T> callback);

    IEnumerable<T> Listen(CancellationToken cancellationToken);

    void Close();
}

public class EventStream<T> : IEventStream<T>
{

    // Closest implementation to a ConcurrentSet<T> in .Net
    private readonly ConcurrentDictionary<Channel<T>, byte> subscribers = new();

    public int SubscriberCount()
    {
        return subscribers.Count;
    }

    public void Emit(T item)
    {
        foreach (var subscriber in subscribers.Keys)
        {
            subscriber.Writer.TryWrite(item);
        }
    }

    public async Task EmitAsync(T item)
    {
        foreach (var subscriber in subscribers.Keys)
        {
            await subscriber.Writer.WriteAsync(item);
        }
    }

    public CancellationTokenSource RunListenerAsync(
    Func<T, Task> callback)
    {
        var cts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            await foreach (var value in ListenAsync(cts.Token))
            {
                await callback(value);
            }

        }, cts.Token);

        return cts;
    }

    public IAsyncEnumerable<T> ListenAsync(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<T>();
        subscribers.TryAdd(channel, 0);
        return ReadFromChannelAsync(channel, cancellationToken);
    }

    public CancellationTokenSource RunListener(Action<T> callback)
    {
        var cts = new CancellationTokenSource();

        _ = Task.Run(() =>
        {
            foreach (var value in Listen(cts.Token))
            {
                callback(value);
            }
        }, cts.Token);

        return cts;
    }

    public IEnumerable<T> Listen(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<T>();
        subscribers.TryAdd(channel, 0);
        return ReadFromChannel(channel, cancellationToken);
    }

    private async IAsyncEnumerable<T> ReadFromChannelAsync(
    Channel<T> channel,
    [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            // .Net 4.8 friendly way of reading from the channel
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    yield return item;

                    // Check cancellation between iterations
                    if (cancellationToken.IsCancellationRequested)
                    {
                        yield break;
                    }
                }
            }
        }
        finally
        {
            RemoveSubscriber(channel);
        }
    }

    private IEnumerable<T> ReadFromChannel(Channel<T> channel, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (channel.Reader.WaitToReadAsync(cancellationToken).AsTask().Result)
                {
                    while (channel.Reader.TryRead(out var item))
                    {
                        yield return item;
                    }
                }
            }
        }
        finally
        {
            RemoveSubscriber(channel);
        }
    }

    public void Close()
    {
        foreach (var subscriber in subscribers.Keys)
        {
            subscriber.Writer.TryComplete();
            RemoveSubscriber(subscriber);
        }
    }

    private void RemoveSubscriber(Channel<T> channel)
    {
        subscribers.TryRemove(channel, out _);
    }
}