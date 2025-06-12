using System.Collections.ObjectModel;
using PowerSync.Common.Client;
using TodoSQLite.Data;
using TodoSQLite.Models;

namespace TodoSQLite.Views;

public partial class TodoListPage : ContentPage
{
    public readonly PowerSyncData _database;
    private readonly TodoList _list;

    public TodoListPage(PowerSyncData database, TodoList list)
    {
        InitializeComponent();
        _database = database;
        _list = list;
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _database._db.Watch("select * from todos", null, new WatchHandler<TodoItem>
        {
            OnResult = (results) =>
            {
                MainThread.BeginInvokeOnMainThread(() => { TodoItemsCollection.ItemsSource = results.ToList(); });
            },
            OnError = (error) =>
            {
                Console.WriteLine("Error: " + error.Message);
            }
        });
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        string description = await DisplayPromptAsync("New Todo", "Enter todo description:");
        if (!string.IsNullOrWhiteSpace(description))
        {
            var todo = new TodoItem 
            { 
                Description = description,
                ListId = _list.ID
            };
            await _database.SaveItemAsync(todo);
        }
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        var button = (Button)sender;
        var todo = (TodoItem)button.CommandParameter;
        
        bool confirm = await DisplayAlert("Confirm Delete", 
            $"Are you sure you want to delete '{todo.Description}'?", 
            "Yes", "No");
            
        if (confirm)
        {
            await _database.DeleteItemAsync(todo);
        }
    }

    private async void OnCheckBoxChanged(object sender, CheckedChangedEventArgs e)
    {
        if (sender is CheckBox checkBox && 
            checkBox.Parent?.Parent?.BindingContext is TodoItem todo)
        {
            if (e.Value == true && todo.CompletedAt == null)
            {
                todo.Completed = e.Value;
                todo.CompletedAt =  DateTime.UtcNow.ToString("o");
                await _database.SaveItemAsync(todo);
            } else if (e.Value == false && todo.CompletedAt != null)
            {
                todo.Completed = e.Value;
                todo.CompletedAt = null; // Uncheck, clear completed time
                await _database.SaveItemAsync(todo);
            }
        }
    }

    private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is TodoItem selectedItem)
        {
            string newDescription = await DisplayPromptAsync("Edit Todo", 
                "Enter new description:", 
                initialValue: selectedItem.Description);
                
            if (!string.IsNullOrWhiteSpace(newDescription))
            {
                selectedItem.Description = newDescription;
                await _database.SaveItemAsync(selectedItem);
            }
            
            TodoItemsCollection.SelectedItem = null;
        }
    }
}

