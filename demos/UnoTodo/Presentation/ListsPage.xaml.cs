namespace UnoTodo.Presentation;

public sealed partial class ListsPage : Page
{
    private PowerSyncData Data => AppServices.PowerSyncData;

    public ListsPage()
    {
        this.InitializeComponent();
    }

    private async void OnAddClicked(object sender, RoutedEventArgs e)
    {
        var name = await Dialogs.PromptTextAsync(this, "New List", "Enter list name:");
        if (!string.IsNullOrWhiteSpace(name))
        {
            await Data.SaveListAsync(new TodoList { Name = name });
        }
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoList list })
        {
            var confirm = await Dialogs.ConfirmAsync(this, "Confirm Delete",
                $"Are you sure you want to delete the list '{list.Name}'?");

            if (confirm)
            {
                await Data.DeleteListAsync(list);
            }
        }
    }
}
