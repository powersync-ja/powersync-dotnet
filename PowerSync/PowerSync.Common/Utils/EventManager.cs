namespace PowerSync.Common.Utils;

public interface IEventManager : ICloseable
{
    /// <summary>
    /// Registers a new EventStream into the EventManager.
    /// </summary>
    void Register<T>(EventStream<T> stream);

    /// <summary>
    /// <para>Deregisters the stream associated with the EventManager.</para>
    /// <para>This does NOT close the stream in the default implementation.</para>
    /// </summary>
    bool Deregister<T>();

    /// <summary>
    /// Attempts to retreive the EventStream associated with events of type T.
    /// </summary>
    bool TryGetStream<T>(out EventStream<T> stream);

    /// <summary>
    /// Posts a message to the stream managing events of type T.
    /// </summary>
    void Emit<T>(T evt);

    /// <summary>
    /// Attemps to post a message to the stream managing events of type T.
    /// </summary>
    bool TryEmit<T>(T evt);
}

public class EventManager : IEventManager
{
    private readonly Dictionary<Type, object> _streams = new();

    public void Register<T>(EventStream<T> stream)
    {
        _streams[typeof(T)] = stream;
    }

    public bool Deregister<T>()
    {
        return _streams.Remove(typeof(T));
    }

    public bool TryGetStream<T>(out EventStream<T> stream)
    {
        if (_streams.TryGetValue(typeof(T), out var streamObj))
        {
            stream = (EventStream<T>)streamObj;
            return true;
        }
        stream = null!;
        return false;
    }

    public void Emit<T>(T evt)
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

    public bool TryEmit<T>(T evt)
    {
        if (TryGetStream<T>(out var stream))
        {
            stream.Emit(evt);
            return true;
        }
        else
        {
            return false;
        }
    }

    public void Close()
    {
        foreach (var kvp in _streams)
        {
            ((ICloseable)kvp.Value).Close();
        }
        _streams.Clear();
    }
}

