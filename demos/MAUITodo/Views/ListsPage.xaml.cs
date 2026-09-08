using MAUITodo.Data;
using MAUITodo.Models;

using PowerSync.Common.Client;
using PowerSync.Common.Client.Sync;

namespace MAUITodo.Views;

public partial class ListsPage
{
    private readonly PowerSyncData database;
    private CancellationTokenSource? _watchCts;

    public ListsPage(PowerSyncData powerSyncData)
    {
        InitializeComponent();
        database = powerSyncData;
        WifiStatusItem.IconImageSource = "wifi_off.png";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _watchCts?.Cancel();
        _watchCts = new CancellationTokenSource();
        var ct = _watchCts.Token;

        _ = Task.Run(async () =>
        {
            await foreach (var update in database.Db.Events.OnStatusChanged.ListenAsync(ct))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    WifiStatusItem.IconImageSource = update.Status.Connected ? "wifi.png" : "wifi_off.png";
                });
            }
        }, ct);

        var listener = database.Db.Watch<TodoList>("select * from lists", null, new() { TriggerImmediately = true, Signal = ct });
        _ = Task.Run(async () =>
        {
            await foreach (var results in listener)
            {
                MainThread.BeginInvokeOnMainThread(() => { ListsCollection.ItemsSource = results.ToList(); });
            }
        }, ct);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _watchCts?.Cancel();
    }

    /// <summary>
    /// Pull-to-refresh: request a checkpoint and wait for the lists to catch up to it.
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
            ListsRefreshView.IsRefreshing = false;
        }
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("New List", "Enter list name:");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var list = new TodoList { Name = name };
            await database.SaveListAsync(list);
        }
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is TodoList list)
        {
            var confirm = await DisplayAlert("Confirm Delete",
                $"Are you sure you want to delete the list '{list.Name}'?",
                "Yes", "No");

            if (confirm)
            {
                await database.DeleteListAsync(list);
            }
        }
    }

    private async void OnListSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is TodoList selectedList)
        {
            await Navigation.PushAsync(new TodoListPage(database, selectedList));
            ListsCollection.SelectedItem = null;
        }
    }
}
