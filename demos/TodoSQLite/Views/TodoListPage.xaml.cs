using System.Collections.ObjectModel;
using TodoSQLite.Data;
using TodoSQLite.Models;

namespace TodoSQLite.Views;

public partial class TodoListPage : ContentPage
{
    private readonly TodoItemDatabase _database;
    private readonly TodoList _list;

    public string ListName => _list.Name;

    public TodoListPage(TodoItemDatabase database, TodoList list)
    {
        InitializeComponent();
        _database = database;
        _list = list;
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        TodoItemsCollection.ItemsSource = await _database.GetItemsAsync(_list.ID);
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        string description = await DisplayPromptAsync("New Todo", "Enter todo description:");
        if (!string.IsNullOrWhiteSpace(description))
        {
            var todo = new TodoItem 
            { 
                Description = description,
                ListId = _list.ID.ToString(),
                CreatedBy = "user" // TODO: Replace with actual user ID
            };
            await _database.SaveItemAsync(todo);
            TodoItemsCollection.ItemsSource = await _database.GetItemsAsync(_list.ID);
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
            TodoItemsCollection.ItemsSource = await _database.GetItemsAsync(_list.ID);
        }
    }

    private async void OnCheckBoxChanged(object sender, CheckedChangedEventArgs e)
    {
        if (sender is CheckBox checkBox && 
            checkBox.Parent?.Parent?.BindingContext is TodoItem todo)
        {
            todo.Completed = e.Value;
            todo.CompletedAt = e.Value ? DateTime.UtcNow.ToString("o") : null;
            todo.CompletedBy = e.Value ? "user" : null; // TODO: Replace with actual user ID
            await _database.SaveItemAsync(todo);
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
                TodoItemsCollection.ItemsSource = await _database.GetItemsAsync(_list.ID);
            }
            
            TodoItemsCollection.SelectedItem = null;
        }
    }
}

