using Windows.Storage.Pickers;

namespace UnoTodo.Presentation;

public sealed partial class TodoPage : Page
{
    private PowerSyncData Data => AppServices.PowerSyncData;

    // Bound via Root.Tag="{Binding List}" so it works regardless of the MVUX-generated
    // bindable wrapper's exact type, matching the same {Binding} path already used for Title/Todos.
    private TodoList CurrentList => (TodoList)Root.Tag;

    public TodoPage()
    {
        this.InitializeComponent();
    }

    private async void OnAddClicked(object sender, RoutedEventArgs e)
    {
        var description = await Dialogs.PromptTextAsync(this, "New Todo", "Enter todo description:");
        if (!string.IsNullOrWhiteSpace(description))
        {
            await Data.SaveItemAsync(new TodoItem
            {
                Description = description,
                ListId = CurrentList.ID,
            });
        }
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoItem todo })
        {
            var confirm = await Dialogs.ConfirmAsync(this, "Confirm Delete",
                $"Are you sure you want to delete '{todo.Description}'?");

            if (confirm)
            {
                await Data.DeleteItemAsync(todo);
            }
        }
    }

    private async void OnPhotoClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TodoItem item })
        {
            return;
        }

        try
        {
            const string takePhoto = "Take photo";
            const string choosePhoto = "Choose photo";

            var removeOption = item.PhotoId != null ? "Remove photo" : null;
            var takeOption = CameraCapture.IsAvailable ? takePhoto : null;
            var choice = await Dialogs.ShowActionSheetAsync(
                this, "Photo", "Cancel", takeOption, choosePhoto, removeOption);

            if (choice != null && choice == removeOption)
            {
                await Data.RemoveTodoPhotoAsync(item.ID, item.PhotoId!);
                return;
            }

            if (choice == takePhoto)
            {
                using var photo = await CameraCapture.CapturePhotoAsync();
                if (photo == null)
                {
                    return;
                }

                await Data.SaveTodoPhotoAsync(item.ID, photo);
            }
            else if (choice == choosePhoto)
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                };
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");

                var file = await picker.PickSingleFileAsync();
                if (file == null)
                {
                    return;
                }

                using var stream = await file.OpenStreamForReadAsync();
                await Data.SaveTodoPhotoAsync(item.ID, stream);
            }
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync(this, "Error", $"Could not update photo: {ex.Message}");
        }
    }

    private async void OnCheckBoxChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: TodoItem todo } checkBox)
        {
            var completed = checkBox.IsChecked == true;
            if (completed && todo.CompletedAt == null)
            {
                await Data.SaveTodoCompletedAsync(todo.ID, true);
            }
            else if (!completed && todo.CompletedAt != null)
            {
                await Data.SaveTodoCompletedAsync(todo.ID, false);
            }
        }
    }

    private async void OnItemSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is TodoItem selectedItem)
        {
            var newDescription = await Dialogs.PromptTextAsync(this, "Edit Todo",
                "Enter new description:", selectedItem.Description);

            if (!string.IsNullOrWhiteSpace(newDescription))
            {
                selectedItem.Description = newDescription;
                await Data.SaveItemAsync(selectedItem);
            }

            TodoItemsCollection.SelectedItem = null;
        }
    }
}
