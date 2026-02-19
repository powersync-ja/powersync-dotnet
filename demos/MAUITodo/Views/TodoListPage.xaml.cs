using MAUITodo.Data;
using MAUITodo.Models;

using PowerSync.Common.Client;

namespace MAUITodo.Views;

public partial class TodoListPage
{
    private readonly PowerSyncData database;
    private readonly TodoList selectedList;

    public TodoListPage(PowerSyncData powerSyncData, TodoList list)
    {
        InitializeComponent();
        database = powerSyncData;
        selectedList = list;
        BindingContext = this;
    }

    public string ListName => selectedList?.Name ?? "";

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var listener = database.Db.Watch<TodoItem>("select * from todos where list_id = ?", [selectedList.ID], new() { TriggerImmediately = true });
        _ = Task.Run(async () =>
        {
            await foreach (var results in listener)
            {
                MainThread.BeginInvokeOnMainThread(() => { TodoItemsCollection.ItemsSource = results.ToList(); });
            }
        });
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

    private async void OnCheckBoxChanged(object sender, CheckedChangedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.Parent?.Parent?.BindingContext is TodoItem todo)
        {
            if (e.Value && todo.CompletedAt == null)
            {
                todo.Completed = e.Value;
                await database.SaveTodoCompletedAsync(todo.ID, true);
            }
            else if (e.Value == false && todo.CompletedAt != null)
            {
                todo.Completed = e.Value;
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
