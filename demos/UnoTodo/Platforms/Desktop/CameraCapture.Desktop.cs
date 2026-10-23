namespace UnoTodo.Services;

public static partial class CameraCapture
{
    // No desktop camera support: callers fall back to picking a photo from disk.
    private static partial bool GetIsAvailable() => false;

    private static partial Task<Stream?> CaptureCoreAsync() => Task.FromResult<Stream?>(null);
}
