namespace UnoTodo.Data;

public static class AppPaths
{
    // Deliberately not ApplicationData.Current, which requires packaged app identity that
    // an unpackaged Uno Skia desktop app does not have.
    public static string AppDataDir { get; } = Directory.CreateDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnoTodo")).FullName;
}
