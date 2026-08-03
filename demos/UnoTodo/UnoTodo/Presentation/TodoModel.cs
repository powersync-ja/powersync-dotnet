using System.Runtime.CompilerServices;

using PowerSync.Common.Client;

namespace UnoTodo.Presentation;

public partial record TodoModel(TodoList List, PowerSyncData Data)
{
    public string Title => List.Name;

    public IListFeed<TodoItem> Todos => ListFeed.AsyncEnumerable<TodoItem>(WatchTodos);

    private async IAsyncEnumerable<IImmutableList<TodoItem>> WatchTodos(
        [EnumeratorCancellation] CancellationToken ct)
    {
        // attachments is a local-only table; the LEFT JOIN surfaces the locally-synced file path
        // (if any) for each todo's photo as `photo_local_uri`, which maps to TodoItem.PhotoLocalUri.
        var stream = Data.Db.Watch<TodoItem>(
            @"SELECT todos.*, attachments.local_uri AS photo_local_uri
              FROM todos
              LEFT JOIN attachments ON todos.photo_id = attachments.id
              WHERE todos.list_id = ?",
            [List.ID],
            new SQLWatchOptions { TriggerImmediately = true, Signal = ct });
        await foreach (var rows in stream.WithCancellation(ct))
        {
            yield return rows.ToImmutableList();
        }
    }
}
