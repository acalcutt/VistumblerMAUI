using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Views;
using VistumblerMAUI.Platforms.Android;

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
    // The system folder picker (SafStorage.PickFolderAsync) reports back here
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == SafStorage.PickFolderRequestCode)
            SafStorage.OnPickFolderResult(resultCode, data);
    }
}
