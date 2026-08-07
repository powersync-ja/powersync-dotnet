namespace UnoTodo.Services;

/// <summary>
/// Captures a photo with the device camera, where the platform supports one.
/// <para>
/// Each target supplies its own implementation under <c>Platforms/&lt;Target&gt;/CameraCapture.&lt;Target&gt;.cs</c>
/// (the Uno SDK only compiles the folder matching the current target framework), so adding a
/// platform means adding one file implementing <see cref="GetIsAvailable"/> and
/// <see cref="CaptureCoreAsync"/> — nothing here or at the call sites changes.
/// </para>
/// </summary>
public static partial class CameraCapture
{
    /// <summary>
    /// Whether this device can capture a photo. Call sites hide the capture option when false.
    /// </summary>
    public static bool IsAvailable => GetIsAvailable();

    /// <summary>
    /// Launches the platform camera UI and returns the captured JPEG, or null if the camera is
    /// unavailable or the user cancelled.
    /// </summary>
    public static Task<Stream?> CapturePhotoAsync() => CaptureCoreAsync();

    private static partial bool GetIsAvailable();

    private static partial Task<Stream?> CaptureCoreAsync();
}
