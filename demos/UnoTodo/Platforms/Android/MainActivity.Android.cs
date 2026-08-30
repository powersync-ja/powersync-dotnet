using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;

namespace UnoTodo.Droid;

[Activity(
    MainLauncher = true,
    ConfigurationChanges = ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden
)]
public class MainActivity : ApplicationActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

        base.OnCreate(savedInstanceState);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        CameraCapture.OnActivityResult(requestCode, resultCode);
    }
}
