using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VistumblerMAUI.Services;
using VistumblerMAUI.Views;

namespace VistumblerMAUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private const string ScanIntervalKey = "Scan_IntervalMs";
    [ObservableProperty] private int  _scanIntervalMs = Preferences.Get(ScanIntervalKey, 1000);
    [ObservableProperty] private bool _soundEnabled   = true;
    [ObservableProperty] private bool _gpsEnabled     = true;

    // Persist the scan interval so the scan loop can honour it (min cadence between scans).
    partial void OnScanIntervalMsChanged(int value) => Preferences.Set(ScanIntervalKey, Math.Max(250, value));

    // Hold the screen awake while scanning/GPS runs (applied on the next scan/GPS
    // start). Background collection works either way via the keep-alive service —
    // this is just the vistumbler-android-style convenience for watching live.
    [ObservableProperty] private bool _keepScreenOn = Preferences.Get("keep_screen_on", false);
    partial void OnKeepScreenOnChanged(bool value) => Preferences.Set("keep_screen_on", value);

    // ── Advanced ─────────────────────────────────────────────────────────────────
    // Opt-in diagnostic logging (GPS fixes, map layer refreshes) via DebugLog.
    [ObservableProperty] private bool _debugLogging = DebugLog.Enabled;
    partial void OnDebugLoggingChanged(bool value) => DebugLog.Enabled = value;

    // ── GPS source (Windows Location API vs serial NMEA receiver) ───────────────
    public const string GpsSourceWindows = "Windows Location";
    public const string GpsSourceSerial  = "Serial NMEA (COM port)";
    public IReadOnlyList<string> GpsSourceOptions { get; } = new[] { GpsSourceWindows, GpsSourceSerial };
    public IReadOnlyList<int>    BaudRateOptions  { get; } = GpsSettings.BaudRates;

    [ObservableProperty] private string _selectedGpsSource =
        GpsSettings.Source == GpsSource.SerialNmea ? GpsSourceSerial : GpsSourceWindows;
    [ObservableProperty] private bool _isSerialGps = GpsSettings.Source == GpsSource.SerialNmea;
    [ObservableProperty] private string _selectedComPort = GpsSettings.ComPort;
    [ObservableProperty] private int    _selectedBaudRate = GpsSettings.BaudRate;

    public ObservableCollection<string> ComPorts { get; } = new();

    partial void OnSelectedGpsSourceChanged(string value)
    {
        IsSerialGps = value == GpsSourceSerial;
        GpsSettings.Source = IsSerialGps ? GpsSource.SerialNmea : GpsSource.WindowsLocation;
        if (IsSerialGps) RefreshComPorts();
    }

    partial void OnSelectedComPortChanged(string value)  => GpsSettings.ComPort  = value ?? string.Empty;
    partial void OnSelectedBaudRateChanged(int value)    => GpsSettings.BaudRate = value;

    [RelayCommand]
    private void RefreshComPorts()
    {
        ComPorts.Clear();
        foreach (var p in GpsSettings.AvailablePorts()) ComPorts.Add(p);
        // Keep a previously-saved port visible even if it's not currently enumerated.
        if (!string.IsNullOrEmpty(SelectedComPort) && !ComPorts.Contains(SelectedComPort))
            ComPorts.Add(SelectedComPort);
    }

    // ── Map style ─────────────────────────────────────────────────────────────
    // Preset names plus a "Custom…" entry; the chosen style URL persists via MapStyles.
    public IReadOnlyList<string> MapStyleOptions { get; } =
        MapStyles.Presets.Select(p => p.Name).Append(MapStyles.CustomName).ToList();

    [ObservableProperty] private string _selectedMapStyle = string.Empty;
    [ObservableProperty] private string _customMapStyleUrl = string.Empty;
    [ObservableProperty] private bool   _isCustomMapStyle;

    // ── Map AP colors ─────────────────────────────────────────────────────────
    // One row per bucket (live active/dead + WifiDB history tiers), each with Open/WEP/
    // Secure hex colors. Rows persist immediately via MapColors; the map picks up the
    // change when the Map page next appears. See MapBucketColorRow / MapColors.
    public ObservableCollection<MapBucketColorRow> MapBucketColors { get; } =
        new(MapColors.Buckets.Select(b => new MapBucketColorRow(b.Key, b.Name)));

    public SettingsViewModel()
    {
        var url    = MapStyles.StyleUrl;
        var preset = MapStyles.Presets.FirstOrDefault(p => p.Url == url);
        if (preset.Name is not null)
        {
            _selectedMapStyle = preset.Name;
        }
        else
        {
            _selectedMapStyle  = MapStyles.CustomName;
            _customMapStyleUrl = url;
            _isCustomMapStyle  = true;
        }

        RefreshComPorts();
    }

    partial void OnSelectedMapStyleChanged(string value)
    {
        if (value == MapStyles.CustomName)
        {
            IsCustomMapStyle = true;
            if (!string.IsNullOrWhiteSpace(CustomMapStyleUrl))
                MapStyles.StyleUrl = CustomMapStyleUrl;
        }
        else
        {
            IsCustomMapStyle = false;
            var preset = MapStyles.Presets.FirstOrDefault(p => p.Name == value);
            if (preset.Url is not null)
                MapStyles.StyleUrl = preset.Url;
        }
    }

    partial void OnCustomMapStyleUrlChanged(string value)
    {
        if (IsCustomMapStyle && !string.IsNullOrWhiteSpace(value))
            MapStyles.StyleUrl = value;
    }

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
