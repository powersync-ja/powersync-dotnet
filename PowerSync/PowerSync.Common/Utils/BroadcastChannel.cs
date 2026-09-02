using System.Threading.Channels;
using System.Collections.Concurrent;

namespace PowerSync.Common.Utils;

/// <summary>
/// <see cref="Channel" />-like object that allows multiple listeners at once and
/// broadcasts messages to all subscribers instead of sending any given message to
/// exactly one consumer.
/// </summary>
internal class BroadcastChannel<T>
{
    private readonly ConcurrentDictionary<Guid, ChannelWriter<T>> _subscribers = new();

    public ChannelReader<T> Subscribe(out Guid subscriberId)
    {
        subscriberId = Guid.NewGuid();
        var ch = Channel.CreateUnbounded<T>();
        _subscribers.TryAdd(subscriberId, ch.Writer);
        return ch.Reader;
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var writer))
        {
            writer.Complete();
        }
    }

    public void Broadcast(T message)
    {
        foreach (ChannelWriter<T> writer in _subscribers.Values)
        {
            writer.TryWrite(message);
        }
    }

    public async Task BroadcastAsync(T message)
    {
        foreach (ChannelWriter<T> writer in _subscribers.Values)
        {
            await writer.WriteAsync(message);
        }
    }
}

