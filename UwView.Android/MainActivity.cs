using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace UwView.Android;

[Activity(
    Label = "UwView.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
