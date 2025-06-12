using PowerSync.Common.Client;
using TodoSQLite.Models;
using TodoSQLite.Data;

namespace TodoSQLite.Views;

public partial class ListsPage : ContentPage
{
    private readonly PowerSyncData _database;
    private bool connected = false;

    public ListsPage(PowerSyncData database)
    {
        InitializeComponent();
        _database = database;
        UpdateWifiStatus();
    }

    private void UpdateWifiStatus()
    {
        WifiStatusItem.IconImageSource = connected ? "wifi.png" : "wifi_off.png";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _database.Init();
        
        await _database._db.Watch("select * from lists", null, new WatchHandler<TodoList>
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
        string name = await DisplayPromptAsync("New List", "Enter list name:");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var list = new TodoList { Name = name };
            await _database.SaveListAsync(list);
        }
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        var button = (Button)sender;
        var list = (TodoList)button.CommandParameter;

        bool confirm = await DisplayAlert("Confirm Delete",
            $"Are you sure you want to delete the list '{list.Name}'?",
            "Yes", "No");

        if (confirm)
        {
            await _database.DeleteListAsync(list);
        }
    }

    private async void OnListSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is TodoList selectedList)
        {
            await Navigation.PushAsync(new TodoListPage(_database, selectedList));
            ListsCollection.SelectedItem = null;
        }
    }
}