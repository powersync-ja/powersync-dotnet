namespace Common.Utils;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

public class EventStream<T>
{
    public ConcurrentBag<Channel<T>> subscribers = [];


    public void Emit(T item)
    {
        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryWrite(item);
        }
    }

    public async Task EmitAsync(T item)
    {
        foreach (var subscriber in subscribers)
        {
            await subscriber.Writer.WriteAsync(item);
        }
    }

    public IAsyncEnumerable<T> ListenAsync(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<T>();
        subscribers.Add(channel);
        return ReadFromChannelAsync(channel, cancellationToken);
    }

    public IEnumerable<T> Listen(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<T>();
        subscribers.Add(channel);
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

    private void RemoveSubscriber(Channel<T> channel)
    {
        // Workaround for ConcurrentBag<T> (cannot remove items directly), maybe a dictionary/set?
        subscribers = [.. subscribers.Where(c => c != channel)];
    }
}