namespace UnoTodo.Presentation;

public static class Dialogs
{
    public static async Task<string?> PromptTextAsync(
        FrameworkElement owner, string title, string placeholder, string? initialValue = null)
    {
        var textBox = new TextBox
        {
            PlaceholderText = placeholder,
            Text = initialValue ?? string.Empty,
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = owner.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }

    public static async Task<bool> ConfirmAsync(
        FrameworkElement owner, string title, string message, string confirmText = "Yes", string cancelText = "No")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = owner.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public static async Task ShowErrorAsync(FrameworkElement owner, string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = owner.XamlRoot,
        };

        await dialog.ShowAsync();
    }

    /// <summary>
    /// Shows an action sheet listing each non-null option as a button, returning the text of the
    /// option that was pressed, or null if dismissed.
    /// </summary>
    public static async Task<string?> ShowActionSheetAsync(
        FrameworkElement owner, string title, string cancelText, params string?[] options)
    {
        var choices = new StackPanel { Spacing = 8 };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = choices,
            CloseButtonText = cancelText,
            XamlRoot = owner.XamlRoot,
        };

        string? selected = null;
        foreach (var option in options)
        {
            if (string.IsNullOrEmpty(option))
            {
                continue;
            }

            var button = new Button
            {
                Content = option,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            button.Click += (_, _) =>
            {
                selected = option;
                dialog.Hide();
            };
            choices.Children.Add(button);
        }

        await dialog.ShowAsync();
        return selected;
    }
}
