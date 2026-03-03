namespace PowerSync.Common.Utils;

public interface IEventManager<TEvent> where TEvent : class
{
    /// <summary>
    /// Attempts to retreive the EventStream associated with events of type T.
    /// </summary>
    bool TryGetStream<T>(out EventStream<T> stream)
        where T : class, TEvent;

    /// <summary>
    /// Posts a message to the stream managing events of type T.
    /// </summary>
    void Emit<T>(T evt)
        where T : class, TEvent;

    /// <summary>
    /// Close all EventStream objects and disable the IEventManager.
    /// </summary>
    void Close();
}

public abstract class EventManager<TEvent> : IEventManager<TEvent>
    where TEvent : class
{
    public abstract bool TryGetStream<T>(out EventStream<T> stream)
        where T : class, TEvent;

    public void Emit<T>(T evt) where T : class, TEvent
    {
        if (TryGetStream<T>(out var stream))
        {
            stream.Emit(evt);
        }
        else
        {
            throw new InvalidOperationException($"No stream emits events of type {nameof(T)}.");
        }
    }

    public abstract void Close();
}

