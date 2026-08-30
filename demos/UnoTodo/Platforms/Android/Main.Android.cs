using Android.App;
using Android.Runtime;

namespace UnoTodo.Droid;

[Application(
    Label = "@string/ApplicationName",
    Icon = "@mipmap/icon",
    LargeHeap = true,
    HardwareAccelerated = true,
    Theme = "@style/Theme.App.Starting"
)]
public class Application : NativeApplication
{
    public Application(IntPtr javaReference, JniHandleOwnership transfer)
        : base(() => new App(), javaReference, transfer) { }
}

