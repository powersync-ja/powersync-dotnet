using PowerSync.Common.Client;
using MAUITodo.Models;
using MAUITodo.Data;

namespace MAUITodo.Views;

public partial class ListsPage
{
    private readonly PowerSyncData database;

    public ListsPage(PowerSyncData powerSyncData)
    {
        InitializeComponent();
        database = powerSyncData;
        WifiStatusItem.IconImageSource = "wifi_off.png";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        database.Db.RunListener((update) =>
        {
            if (update.StatusChanged != null)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    WifiStatusItem.IconImageSource = update.StatusChanged.Connected ? "wifi.png" : "wifi_off.png";
                });

            }
        });
        
        await database.Db.Watch("select * from lists", null, new WatchHandler<TodoList>
        {
            OnResult = (results) =>
            {
                MainThread.BeginInvokeOnMainThread(() => { ListsCollection.ItemsSource = results.ToList(); });
            },
            OnError = (error) =>
            {
                Console.WriteLine("Error: " + error.Message);
            }
        });
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