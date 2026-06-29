using Android.App;
using Android.Content.PM;
using Android.Views;

namespace VistumblerMAUI;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode,
    WindowSoftInputMode = SoftInput.AdjustPan,
    Exported = true)]
public class MainActivity : MauiAppCompatActivity
{
}
