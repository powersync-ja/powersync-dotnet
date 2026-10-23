using System.Runtime.CompilerServices;

using PowerSync.Common.Client;

namespace UnoTodo.Presentation;

public partial record ListsModel(PowerSyncData Data)
{
    public IListFeed<TodoList> Lists => ListFeed.AsyncEnumerable<TodoList>(WatchLists);

    public IFeed<bool> Connected => Feed.AsyncEnumerable<bool>(WatchConnected);

    private async IAsyncEnumerable<IImmutableList<TodoList>> WatchLists(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = Data.Db.Watch<TodoList>("select * from lists", null,
            new SQLWatchOptions { TriggerImmediately = true, Signal = ct });
        await foreach (var rows in stream.WithCancellation(ct))
        {
            yield return rows.ToImmutableList();
        }
    }

    private async IAsyncEnumerable<bool> WatchConnected([EnumeratorCancellation] CancellationToken ct)
    {
        yield return Data.Db.Connected;
        await foreach (var update in Data.Db.Events.OnStatusChanged.ListenAsync(ct))
        {
            yield return update.Status.Connected;
        }
    }
}
