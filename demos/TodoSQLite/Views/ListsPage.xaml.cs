using TodoSQLite.Models;
using TodoSQLite.Data;

namespace TodoSQLite.Views;

public partial class ListsPage : ContentPage
{
    private readonly PowerSyncData _database;

    public ListsPage(PowerSyncData database)
    {
        InitializeComponent();
        _database = database;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ListsCollection.ItemsSource = await _database.GetListsAsync();
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        string name = await DisplayPromptAsync("New List", "Enter list name:");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var list = new TodoList { Name = name };
            await _database.SaveListAsync(list);
            ListsCollection.ItemsSource = await _database.GetListsAsync();
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
            ListsCollection.ItemsSource = await _database.GetListsAsync();
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