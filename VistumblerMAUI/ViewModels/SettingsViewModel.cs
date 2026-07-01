using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VistumblerMAUI.Services;
using VistumblerMAUI.Views;

namespace VistumblerMAUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    [ObservableProperty] private int  _scanIntervalMs = 2000;
    [ObservableProperty] private bool _soundEnabled   = true;
    [ObservableProperty] private bool _gpsEnabled     = true;

    // ── WifiDB ──────────────────────────────────────────────────────────────
    // Backed by WifiDbSettings (MAUI Preferences) so they persist and are readable
    // by MapViewModel when it requests history tiles.
    [ObservableProperty] private string _wifiDbUrl    = WifiDbSettings.Url;
    [ObservableProperty] private string _wifiDbUser   = WifiDbSettings.User;
    [ObservableProperty] private string _wifiDbApiKey = WifiDbSettings.ApiKey;
    [ObservableProperty] private string _wifiDbStatus = string.Empty;

    partial void OnWifiDbUrlChanged(string value)    => WifiDbSettings.Url    = value;
    partial void OnWifiDbUserChanged(string value)   => WifiDbSettings.User   = value;
    partial void OnWifiDbApiKeyChanged(string value) => WifiDbSettings.ApiKey = value;

    /// <summary>Re-read the persisted WifiDB fields (e.g. after returning from the QR scanner).</summary>
    public void Reload()
    {
        WifiDbUrl    = WifiDbSettings.Url;
        WifiDbUser   = WifiDbSettings.User;
        WifiDbApiKey = WifiDbSettings.ApiKey;
    }

    /// <summary>Open the camera to scan a WifiDB registration QR code (mobile).</summary>
    [RelayCommand]
    private async Task ScanWifiDbQrAsync() => await Shell.Current.GoToAsync(nameof(WifiDbScanPage));

    [RelayCommand]
    private async Task GoToImportAsync() => await Shell.Current.GoToAsync(nameof(ImportPage));

    [RelayCommand]
    private async Task GoToExportAsync() => await Shell.Current.GoToAsync(nameof(ExportPage));

    /// <summary>
    /// Register with WifiDB by redeeming a one-time link. On mobile this normally comes from
    /// the registration QR code (see ScanWifiDbQr); this entry point accepts the link directly,
    /// which also covers desktop where there is no camera.
    /// </summary>
    [RelayCommand]
    private async Task RedeemWifiDbLinkAsync()
    {
        var link = await Shell.Current.DisplayPromptAsync(
            "WifiDB registration",
            "Paste your WifiDB registration link (…/redeem_link.php?token=…):",
            accept: "Redeem", cancel: "Cancel", keyboard: Keyboard.Url);

        if (!string.IsNullOrWhiteSpace(link))
            await RedeemAsync(link);
    }

    /// <summary>
    /// Redeems a WifiDB registration link and applies the returned credentials to the
    /// WifiDB fields (which persist via the OnChanged handlers). Shared by the paste-link
    /// command above and the QR scanner.
    /// </summary>
    public async Task RedeemAsync(string redeemUrl)
    {
        if (!WifiDbRegistration.IsRedeemLink(redeemUrl))
        {
            WifiDbStatus = "That doesn't look like a WifiDB registration link.";
            return;
        }

        WifiDbStatus = "Redeeming…";
        try
        {
            var cred = await WifiDbRegistration.RedeemAsync(redeemUrl, _http);
            if (!string.IsNullOrWhiteSpace(cred.BaseUrl))  WifiDbUrl  = cred.BaseUrl;
            if (!string.IsNullOrWhiteSpace(cred.Username)) WifiDbUser = cred.Username;
            WifiDbApiKey = cred.ApiKey;
            WifiDbStatus = string.IsNullOrWhiteSpace(cred.Username)
                ? "Registered with WifiDB."
                : $"Registered with WifiDB as {cred.Username}.";
        }
        catch (Exception ex)
        {
            WifiDbStatus = $"Registration failed: {ex.Message}";
        }
    }
}
