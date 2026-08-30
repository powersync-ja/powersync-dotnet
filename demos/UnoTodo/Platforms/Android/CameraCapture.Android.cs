using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Provider;

using AndroidX.Core.Content;

namespace UnoTodo.Services;

public static partial class CameraCapture
{
    // Must match the provider authority declared in Platforms/Android/AndroidManifest.xml.
    private const string FileProviderAuthority = "com.powersync.unotodo.fileprovider";
    private const int CaptureRequestCode = 0x0CA3;

    private static TaskCompletionSource<bool>? pendingCapture;

    private static partial bool GetIsAvailable()
    {
        if (Uno.UI.ContextHelper.Current is not Context { PackageManager: { } packageManager })
        {
            return false;
        }

        // ResolveActivity only sees the camera app thanks to the <queries> entry in the manifest.
        return packageManager.HasSystemFeature(PackageManager.FeatureCameraAny)
            && new Intent(MediaStore.ActionImageCapture).ResolveActivity(packageManager) != null;
    }

    private static async partial Task<Stream?> CaptureCoreAsync()
    {
        if (Uno.UI.ContextHelper.Current is not Activity activity)
        {
            return null;
        }

        // App-private external storage, shared with the camera app through a FileProvider grant,
        // so neither the CAMERA nor any storage permission is needed.
        var directory = new Java.IO.File(activity.GetExternalFilesDir(null), "Pictures");
        directory.Mkdirs();
        var file = new Java.IO.File(directory, $"capture-{Guid.NewGuid():N}.jpg");

        try
        {
            if (FileProvider.GetUriForFile(activity, FileProviderAuthority, file) is not { } outputUri)
            {
                return null;
            }

            var intent = new Intent(MediaStore.ActionImageCapture);
            intent.PutExtra(MediaStore.ExtraOutput, (Android.OS.IParcelable)outputUri);
            intent.AddFlags(ActivityFlags.GrantWriteUriPermission);

            var completion = new TaskCompletionSource<bool>();
            pendingCapture = completion;

            activity.StartActivityForResult(intent, CaptureRequestCode);

            if (!await completion.Task || !file.Exists() || file.Length() == 0)
            {
                return null;
            }

            return new MemoryStream(await File.ReadAllBytesAsync(file.AbsolutePath!));
        }
        finally
        {
            pendingCapture = null;
            if (file.Exists())
            {
                file.Delete();
            }
        }
    }

    /// <summary>
    /// Called from MainActivity's OnActivityResult to resume a pending capture.
    /// </summary>
    internal static void OnActivityResult(int requestCode, Result resultCode)
    {
        if (requestCode == CaptureRequestCode)
        {
            pendingCapture?.TrySetResult(resultCode == Result.Ok);
        }
    }
}
