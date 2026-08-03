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
    /// Shows a simple action sheet with up to three choices (Primary/Secondary/Close),
    /// returning the text of the button that was pressed, or null if dismissed.
    /// </summary>
    public static async Task<string?> ShowActionSheetAsync(
        FrameworkElement owner, string title, string cancelText, string? primaryText, string? secondaryText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            CloseButtonText = cancelText,
            PrimaryButtonText = primaryText ?? string.Empty,
            SecondaryButtonText = secondaryText ?? string.Empty,
            XamlRoot = owner.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => primaryText,
            ContentDialogResult.Secondary => secondaryText,
            _ => null,
        };
    }
}
