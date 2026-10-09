using VistumblerMAUI.Localization;
using BarcodeScanning;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.Views;

/// <summary>
/// Camera page that scans an account QR code and returns to Settings. By default it reads a WifiDB
/// registration code (a …/redeem_link.php?token=… URL), redeems it for the account's username/API key and
/// persists them via <see cref="WifiDbSettings"/>. With <c>?mode=wigle</c> it reads the code on the WiGLE
/// account page ("username:apiName:apiToken") into <see cref="WigleSettings"/>. Mirrors vistumbler-android's
/// ActivateActivity. Uses BarcodeScanning.Native.Maui (native MLKit/Vision).
/// </summary>
public partial class WifiDbScanPage : ContentPage, IQueryAttributable
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private bool _handled;
    private bool _wigle;

    public WifiDbScanPage()
    {
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _wigle = query.TryGetValue("mode", out var mode) && string.Equals(mode as string, "wigle", StringComparison.OrdinalIgnoreCase);
        Title = Loc.T(_wigle ? "WigleScan_Title" : "WifiDbScan_ScanWifiDBQR");
        StatusLabel.Text = Loc.T(_wigle ? "WigleScan_PointTheCamera" : "WifiDbScan_PointTheCameraAtYour");
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
            StatusLabel.Text = Loc.T(_wigle ? "WigleScan_NeedCamera" : "WifiDbScan_NeedCamera");
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

        if (_wigle)
        {
            foreach (var r in e.BarcodeResults)
            {
                if (!WigleSettings.TryParseActivationCode(r.DisplayValue, out var user, out var apiName, out var apiToken)) continue;
                _handled = true;
                Camera.CameraEnabled = false;
                MainThread.BeginInvokeOnMainThread(async () => await SaveWigleAndReturnAsync(user, apiName, apiToken));
                return;
            }
            return; // keep scanning; ignore other codes
        }

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

    private async Task SaveWigleAndReturnAsync(string user, string apiName, string apiToken)
    {
        WigleSettings.ApiName = apiName;
        WigleSettings.ApiToken = apiToken;
        await DisplayAlert("WiGLE", Loc.T("WigleScan_Saved", string.IsNullOrWhiteSpace(user) ? apiName : user), "OK");
        await Shell.Current.GoToAsync("..");
    }

    private async Task RedeemAndReturnAsync(string link)
    {
        StatusLabel.Text = Loc.T("Settings_Redeeming");
        try
        {
            var cred = await WifiDbRegistration.RedeemAsync(link, _http);
            if (!string.IsNullOrWhiteSpace(cred.BaseUrl))  WifiDbSettings.Url    = cred.BaseUrl;
            if (!string.IsNullOrWhiteSpace(cred.Username)) WifiDbSettings.User   = cred.Username;
            WifiDbSettings.ApiKey = cred.ApiKey;

            await DisplayAlert("WifiDB",
                string.IsNullOrWhiteSpace(cred.Username)
                    ? Loc.T("Settings_Registered")
                    : Loc.T("Settings_RegisteredAs", cred.Username),
                "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _handled = false;                 // allow another attempt
            StatusLabel.Text = Loc.T("Settings_RegistrationFailed", ex.Message);
            Camera.CameraEnabled = true;
        }
    }

    private async void OnCancel(object? sender, EventArgs e)
    {
        Camera.CameraEnabled = false;
        await Shell.Current.GoToAsync("..");
    }
}
