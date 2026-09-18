using MAUITodo.Data;
using MAUITodo.Models;

using PowerSync.Common.Client;
using PowerSync.Common.Client.Sync;

namespace MAUITodo.Views;

public partial class TodoListPage
{
    private readonly PowerSyncData database;
    private readonly TodoList selectedList;
    private CancellationTokenSource? _watchCts;

    public TodoListPage(PowerSyncData powerSyncData, TodoList list)
    {
        InitializeComponent();
        database = powerSyncData;
        selectedList = list;
        BindingContext = this;
    }

    public string ListName => selectedList?.Name ?? "";

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _watchCts?.Cancel();
        _watchCts = new CancellationTokenSource();
        var ct = _watchCts.Token;

        // attachments is a local-only table; the LEFT JOIN surfaces the locally-synced file path
        // (if any) for each todo's photo as `photo_local_uri`, which maps to TodoItem.PhotoLocalUri.
        var listener = database.Db.Watch<TodoItem>(
            @"SELECT todos.*, attachments.local_uri AS photo_local_uri
              FROM todos
              LEFT JOIN attachments ON todos.photo_id = attachments.id
              WHERE todos.list_id = ?",
            [selectedList.ID],
            new() { TriggerImmediately = true, Signal = ct });

        _ = Task.Run(async () =>
        {
            await foreach (var results in listener)
            {
                MainThread.BeginInvokeOnMainThread(() => { TodoItemsCollection.ItemsSource = results.ToList(); });
            }
        }, ct);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _watchCts?.Cancel();
    }

    /// <summary>
    /// Pull-to-refresh: request a checkpoint and wait for this list's todos to catch up to it.
    /// </summary>
    private async void OnRefreshing(object sender, EventArgs e)
    {
        try
        {
            await database.RefreshAsync();
        }
        catch (CheckpointRequestException ex)
        {
            await DisplayAlert("Refresh failed", ex.Message, "OK");
        }
        finally
        {
            TodoItemsRefreshView.IsRefreshing = false;
        }
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var description = await DisplayPromptAsync("New Todo", "Enter todo description:");
        if (!string.IsNullOrWhiteSpace(description))
        {
            var todo = new TodoItem
            {
                Description = description,
                ListId = selectedList.ID
            };
            await database.SaveItemAsync(todo);
        }
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is TodoItem todo)
        {
            var confirm = await DisplayAlert("Confirm Delete",
                $"Are you sure you want to delete '{todo.Description}'?",
                "Yes", "No");

            if (confirm)
            {
                await database.DeleteItemAsync(todo);
            }
        }
    }

    private async void OnPhotoClicked(object sender, EventArgs e)
    {
        var item = sender switch
        {
            Button btn => btn.CommandParameter as TodoItem,
            ImageButton imgBtn => imgBtn.CommandParameter as TodoItem,
            _ => null
        };
        if (item == null) return;

        try
        {
            var remove = item.PhotoId != null ? "Remove photo" : null;
            var choice = await DisplayActionSheet("Photo", "Cancel", remove, "Take photo", "Choose from library");

            if (remove != null && choice == remove)
            {
                await database.RemoveTodoPhotoAsync(item.ID, item.PhotoId!);
                return;
            }

            var photo = choice switch
            {
                "Take photo" => await MediaPicker.Default.CapturePhotoAsync(),
                "Choose from library" => await MediaPicker.Default.PickPhotoAsync(),
                _ => null
            };
            if (photo == null) return;

            await using var stream = await photo.OpenReadAsync();
            await database.SaveTodoPhotoAsync(item.ID, stream);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not update photo: {ex.Message}", "OK");
        }
    }

    private async void OnCheckBoxChanged(object sender, CheckedChangedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.Parent?.Parent?.BindingContext is TodoItem todo)
        {
            if (e.Value && todo.CompletedAt == null)
            {
                await database.SaveTodoCompletedAsync(todo.ID, true);
            }
            else if (e.Value == false && todo.CompletedAt != null)
            {
                await database.SaveTodoCompletedAsync(todo.ID, false);
            }
        }
    }

    private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is TodoItem selectedItem)
        {
            var newDescription = await DisplayPromptAsync("Edit Todo",
                "Enter new description:",
                initialValue: selectedItem.Description);

            if (!string.IsNullOrWhiteSpace(newDescription))
            {
                selectedItem.Description = newDescription;
                await database.SaveItemAsync(selectedItem);
            }

            TodoItemsCollection.SelectedItem = null;
        }
    }
}
