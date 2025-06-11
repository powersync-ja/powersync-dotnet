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
        string name = await DisplayPromptAsync("New Todo", "Enter todo name:");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var todo = new TodoItem 
            { 
                Name = name,
                ListId = _list.ID
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
            $"Are you sure you want to delete '{todo.Name}'?", 
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
            todo.Done = e.Value;
            await _database.SaveItemAsync(todo);
        }
    }

    private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is TodoItem selectedItem)
        {
            string newName = await DisplayPromptAsync("Edit Todo", 
                "Enter new name:", 
                initialValue: selectedItem.Name);
                
            if (!string.IsNullOrWhiteSpace(newName))
            {
                selectedItem.Name = newName;
                await _database.SaveItemAsync(selectedItem);
                TodoItemsCollection.ItemsSource = await _database.GetItemsAsync(_list.ID);
            }
            
            TodoItemsCollection.SelectedItem = null;
        }
    }
}

