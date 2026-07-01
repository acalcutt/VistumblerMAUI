using BarcodeScanning;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.Views;

/// <summary>
/// Camera page that scans a WifiDB registration QR code (a …/redeem_link.php?token=…
/// URL), redeems it for the account's username/API key, persists them via
/// <see cref="WifiDbSettings"/>, and returns to Settings. Mirrors vistumbler-android's
/// ActivateActivity WifiDB flow. Uses BarcodeScanning.Native.Maui (native MLKit/Vision).
/// </summary>
public partial class WifiDbScanPage : ContentPage
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private bool _handled;

    public WifiDbScanPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _handled = false;

        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status == PermissionStatus.Granted)
        {
            Camera.CameraEnabled = true;
        }
        else
        {
            StatusLabel.Text = "Camera permission is required to scan. Use \"Register with link…\" instead.";
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Camera.CameraEnabled = false;
    }

    private void OnDetectionFinished(object? sender, OnDetectionFinishedEventArg e)
    {
        if (_handled || e.BarcodeResults is null) return;

        string? link = null;
        foreach (var r in e.BarcodeResults)
        {
            if (WifiDbRegistration.IsRedeemLink(r.DisplayValue))
            {
                link = r.DisplayValue;
                break;
            }
        }
        if (link is null) return; // keep scanning; ignore non-WifiDB codes

        _handled = true;
        Camera.CameraEnabled = false;

        // Detection callbacks run off the UI thread — hop back before touching UI/navigation.
        MainThread.BeginInvokeOnMainThread(async () => await RedeemAndReturnAsync(link));
    }

    private async Task RedeemAndReturnAsync(string link)
    {
        StatusLabel.Text = "Redeeming…";
        try
        {
            var cred = await WifiDbRegistration.RedeemAsync(link, _http);
            if (!string.IsNullOrWhiteSpace(cred.BaseUrl))  WifiDbSettings.Url    = cred.BaseUrl;
            if (!string.IsNullOrWhiteSpace(cred.Username)) WifiDbSettings.User   = cred.Username;
            WifiDbSettings.ApiKey = cred.ApiKey;

            await DisplayAlert("WifiDB",
                string.IsNullOrWhiteSpace(cred.Username)
                    ? "Registered with WifiDB."
                    : $"Registered with WifiDB as {cred.Username}.",
                "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _handled = false;                 // allow another attempt
            StatusLabel.Text = $"Registration failed: {ex.Message}";
            Camera.CameraEnabled = true;
        }
    }

    private async void OnCancel(object? sender, EventArgs e)
    {
        Camera.CameraEnabled = false;
        await Shell.Current.GoToAsync("..");
    }
}
