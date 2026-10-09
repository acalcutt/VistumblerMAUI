using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VistumblerMAUI.Services;
using VistumblerMAUI.Views;

namespace VistumblerMAUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private const string ScanIntervalKey = "Scan_IntervalMs";
    [ObservableProperty] private int  _scanIntervalMs = Preferences.Get(ScanIntervalKey, 1000);
    [ObservableProperty] private bool _soundEnabled   = SoundSettings.NewApSound;
    partial void OnSoundEnabledChanged(bool value) => SoundSettings.NewApSound = value;

    // ── Sound (SoundSettings) ─────────────────────────────────────────────────
    public IReadOnlyList<string> NewApSoundOptions { get; } =
        new[] { "Once for each scan that finds new APs", "Once for each new AP", "Once for each new AP, louder for a stronger signal" };
    public IReadOnlyList<string> SpeakVoiceOptions { get; } =
        new[] { "The device's voice", "Vistumbler's recorded words", "A tone, higher for a stronger signal" };
    public IReadOnlyList<string> SpeakValueOptions { get; } = new[] { "Signal (%)", "RSSI (dBm)" };
    [ObservableProperty] private string _selectedSpeakValue = string.Empty;
    partial void OnSelectedSpeakValueChanged(string value)
    {
        int i = SpeakValueOptions.ToList().IndexOf(value);
        if (i >= 0) SoundSettings.Value = (SpeakValue)i;
    }

    [ObservableProperty] private string _selectedNewApSound = string.Empty;
    [ObservableProperty] private string _selectedSpeakVoice = string.Empty;
    [ObservableProperty] private bool   _errorSound = SoundSettings.ErrorSound;
    [ObservableProperty] private bool   _sayPercent = SoundSettings.SayPercent;
    [ObservableProperty] private string _speakIntervalSeconds = (SoundSettings.SpeakIntervalMs / 1000.0).ToString("0.#");

    partial void OnSelectedNewApSoundChanged(string value)
    {
        int i = NewApSoundOptions.ToList().IndexOf(value);
        if (i >= 0) SoundSettings.NewApMode = (NewApSoundMode)i;
    }

    partial void OnSelectedSpeakVoiceChanged(string value)
    {
        int i = SpeakVoiceOptions.ToList().IndexOf(value);
        if (i >= 0) SoundSettings.Voice = (SpeakVoice)i;
    }

    partial void OnErrorSoundChanged(bool value) => SoundSettings.ErrorSound = value;
    partial void OnSayPercentChanged(bool value) => SoundSettings.SayPercent = value;

    partial void OnSpeakIntervalSecondsChanged(string value)
    {
        if (double.TryParse(value, out var s) && s > 0) SoundSettings.SpeakIntervalMs = (int)(s * 1000);
    }
    [ObservableProperty] private bool _gpsEnabled     = true;

    // Persist the scan interval so the scan loop can honour it (min cadence between scans).
    partial void OnScanIntervalMsChanged(int value) => Preferences.Set(ScanIntervalKey, Math.Max(250, value));

    // Hold the screen awake while scanning/GPS runs (applied on the next scan/GPS
    // start). Background collection works either way via the keep-alive service —
    // this is just the vistumbler-android-style convenience for watching live.
    [ObservableProperty] private bool _keepScreenOn = Preferences.Get("keep_screen_on", false);
    partial void OnKeepScreenOnChanged(bool value) => Preferences.Set("keep_screen_on", value);

    // ── Start on launch, Wi-Fi adapter, coordinate format (ScanSettings) ─────────
    [ObservableProperty] private bool _scanOnLaunch = ScanSettings.ScanOnLaunch;
    [ObservableProperty] private bool _gpsOnLaunch  = ScanSettings.GpsOnLaunch;
    partial void OnScanOnLaunchChanged(bool value) => ScanSettings.ScanOnLaunch = value;
    partial void OnGpsOnLaunchChanged(bool value)  => ScanSettings.GpsOnLaunch  = value;

    [ObservableProperty] private bool _saveGpsWithoutAps = ScanSettings.SaveGpsWithoutAps;
    partial void OnSaveGpsWithoutApsChanged(bool value) => ScanSettings.SaveGpsWithoutAps = value;

    /// <summary>Only Windows lets the app choose an adapter; phones have one.</summary>
    public bool HasAdapterChoice => OperatingSystem.IsWindows();
    public ObservableCollection<Vistumbler.Core.Models.WiFiAdapter> Adapters { get; } = new();
    [ObservableProperty] private Vistumbler.Core.Models.WiFiAdapter? _selectedAdapter;
    private bool _loadingAdapters;

    partial void OnSelectedAdapterChanged(Vistumbler.Core.Models.WiFiAdapter? value)
    {
        if (value is null || _loadingAdapters) return;
        ScanSettings.AdapterId = value.Id;
        IPlatformApplication.Current?.Services.GetService<Vistumbler.Core.Services.IWiFiScannerService>()?.SetActiveAdapter(value.Id);
    }

    [RelayCommand]
    private async Task RefreshAdaptersAsync()
    {
        var wifi = IPlatformApplication.Current?.Services.GetService<Vistumbler.Core.Services.IWiFiScannerService>();
        if (wifi is null || !HasAdapterChoice) return;
        _loadingAdapters = true;
        try
        {
            Adapters.Clear();
            Adapters.Add(new Vistumbler.Core.Models.WiFiAdapter { Id = string.Empty, Name = "All adapters" });
            foreach (var a in await wifi.GetAvailableAdaptersAsync()) Adapters.Add(a);
            // Keep a chosen adapter that isn't plugged in right now, so the choice isn't lost
            var chosen = Adapters.FirstOrDefault(a => a.Id == ScanSettings.AdapterId);
            if (chosen is null && ScanSettings.AdapterId.Length > 0)
                Adapters.Add(chosen = new Vistumbler.Core.Models.WiFiAdapter { Id = ScanSettings.AdapterId, Name = "Chosen adapter (not found)" });
            SelectedAdapter = chosen ?? Adapters[0];
        }
        catch { /* leave the list as it is */ }
        finally
        {
            _loadingAdapters = false;
        }
    }

    public IReadOnlyList<string> GpsFormatOptions { get; } = new[]
    {
        "Decimal (48.11730, -11.51667)",
        "dd.dddd (N 48.1173000)",
        "ddmm.mmmm (N 4807.0380)",
        "dd mm ss (N 48° 7' 2.28\")",
    };
    [ObservableProperty] private string _selectedGpsFormat = string.Empty;
    partial void OnSelectedGpsFormatChanged(string value)
    {
        int index = GpsFormatOptions.ToList().IndexOf(value);
        if (index >= 0) ScanSettings.GpsFormat = (Vistumbler.Core.Models.GpsDisplayFormat)index;
    }

    // ── Updates ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _autoCheckForUpdates = AppUpdater.AutoCheck;
    partial void OnAutoCheckForUpdatesChanged(bool value) => AppUpdater.AutoCheck = value;

    [ObservableProperty] private bool _includePrereleaseUpdates = AppUpdater.IncludePrereleases;
    partial void OnIncludePrereleaseUpdatesChanged(bool value) => AppUpdater.IncludePrereleases = value;

    public string VersionText => $"{AppUpdater.ProductName} {AppUpdater.CurrentVersion}";
    public bool UpdatesSupported => AppUpdater.IsSupported;
    public bool UpdatesUnsupported => !AppUpdater.IsSupported;
    public string? UpdatesUnsupportedReason => AppUpdater.UnsupportedReason;

    [RelayCommand]
    private Task CheckForUpdatesAsync() =>
        Shell.Current is AppShell shell ? shell.CheckForUpdatesAsync() : Task.CompletedTask;

    // ── Advanced ─────────────────────────────────────────────────────────────────
    // Opt-in diagnostic logging (GPS fixes, map layer refreshes) via DebugLog.
    [ObservableProperty] private bool _debugLogging = DebugLog.Enabled;
    partial void OnDebugLoggingChanged(bool value) => DebugLog.Enabled = value;

    // ── GPS source (the OS location service or an external NMEA receiver) ───────
    // The options depend on the platform (GpsSettings.AvailableSources): Windows Location or a serial COM port
    // on Windows, the phone's GPS or a paired Bluetooth receiver on Android.
    public IReadOnlyList<string> GpsSourceOptions { get; } = GpsSettings.AvailableSources.Select(s => s.Name).ToList();
    public bool HasGpsSourceChoice => GpsSourceOptions.Count > 1;
    public IReadOnlyList<int>    BaudRateOptions  { get; } = GpsSettings.BaudRates;

    [ObservableProperty] private string _selectedGpsSource =
        GpsSettings.AvailableSources.First(s => s.Source == GpsSettings.Source).Name;
    [ObservableProperty] private bool _isSerialGps    = GpsSettings.Source == GpsSource.SerialNmea;
    [ObservableProperty] private bool _isBluetoothGps = GpsSettings.Source == GpsSource.BluetoothNmea;
    [ObservableProperty] private bool _isUsbGps       = GpsSettings.Source == GpsSource.UsbNmea;
    [ObservableProperty] private bool _isExternalGps  = GpsSettings.IsExternalReceiver;
    [ObservableProperty] private bool _reconnectGpsWhenNoData    = GpsSettings.ReconnectWhenNoData;
    [ObservableProperty] private bool _resetGpsPositionWhenNoFix = GpsSettings.ResetPositionWhenNoFix;

    partial void OnReconnectGpsWhenNoDataChanged(bool value)    => GpsSettings.ReconnectWhenNoData    = value;
    partial void OnResetGpsPositionWhenNoFixChanged(bool value) => GpsSettings.ResetPositionWhenNoFix = value;
    [ObservableProperty] private string _selectedComPort = GpsSettings.ComPort;
    [ObservableProperty] private int    _selectedBaudRate = GpsSettings.BaudRate;

    public ObservableCollection<string> ComPorts { get; } = new();

    partial void OnSelectedGpsSourceChanged(string value)
    {
        var source = GpsSettings.AvailableSources.FirstOrDefault(s => s.Name == value).Source;
        GpsSettings.Source = source;
        IsSerialGps    = source == GpsSource.SerialNmea;
        IsBluetoothGps = source == GpsSource.BluetoothNmea;
        IsUsbGps       = source == GpsSource.UsbNmea;
        IsExternalGps  = GpsSettings.IsExternalReceiver;
        if (IsSerialGps) RefreshComPorts();
        if (IsUsbGps) RefreshUsbDevices();
        if (IsBluetoothGps && BluetoothDevices.Count <= 1) _ = RefreshBluetoothDevicesAsync();
    }

    // ── USB GPS receiver (Android) ──────────────────────────────────────────────
    [ObservableProperty] private int    _selectedUsbBaudRate = GpsSettings.UsbBaudRate;
    [ObservableProperty] private string _usbDevicesText = string.Empty;

    partial void OnSelectedUsbBaudRateChanged(int value) => GpsSettings.UsbBaudRate = value;

    /// <summary>Shows which supported USB receivers are plugged in now; GPS uses the first one.</summary>
    [RelayCommand]
    private void RefreshUsbDevices()
    {
        var usb = IPlatformApplication.Current?.Services.GetService<IUsbGpsService>();
        if (usb is null) return;
        try
        {
            var devices = usb.GetAttachedDevices();
            UsbDevicesText = devices.Count == 0
                ? "No supported USB GPS receiver is plugged in."
                : "Plugged in: " + string.Join(", ", devices);
        }
        catch (Exception ex)
        {
            UsbDevicesText = $"Could not list USB devices: {ex.Message}";
        }
    }

    // ── Bluetooth GPS receiver (Android) ────────────────────────────────────────
    public ObservableCollection<BluetoothGpsDevice> BluetoothDevices { get; } = new();
    [ObservableProperty] private BluetoothGpsDevice? _selectedBluetoothDevice;
    [ObservableProperty] private string _bluetoothStatus = string.Empty;

    partial void OnSelectedBluetoothDeviceChanged(BluetoothGpsDevice? value)
    {
        if (value is null) return;
        GpsSettings.BluetoothAddress = value.Address;
        GpsSettings.BluetoothName    = value.Name;
    }

    /// <summary>Lists the paired devices to pick the GPS receiver from; pair it in Android's Bluetooth settings first.</summary>
    [RelayCommand]
    private async Task RefreshBluetoothDevicesAsync()
    {
        var bluetooth = IPlatformApplication.Current?.Services.GetService<IBluetoothGpsService>();
        if (bluetooth is null) return;
        try
        {
            var devices = await bluetooth.GetPairedDevicesAsync();
            BluetoothDevices.Clear();
            foreach (var d in devices) BluetoothDevices.Add(d);
            SelectedBluetoothDevice = devices.FirstOrDefault(d => d.Address == GpsSettings.BluetoothAddress);
            BluetoothStatus = devices.Count == 0
                ? "No paired devices. Pair the GPS receiver in Android's Bluetooth settings, then refresh."
                : string.Empty;
        }
        catch (Exception ex)
        {
            BluetoothStatus = ex.Message;
        }
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

    // ── GPS follow zoom ───────────────────────────────────────────────────────
    // How the map zooms when the GPS control's Follow mode engages. Persists via
    // MapFollowSettings; MapPage applies it to the map control on appearing.
    public IReadOnlyList<string> FollowZoomOptions { get; } =
        new[] { "Auto (fit GPS accuracy)", "Manual zoom level", "Keep current zoom" };

    [ObservableProperty] private string _selectedFollowZoom = string.Empty;
    [ObservableProperty] private string _manualFollowZoom = string.Empty;
    [ObservableProperty] private bool   _isManualFollowZoom;

    // ── Map point size ────────────────────────────────────────────────────────
    [ObservableProperty] private double _mapPointScale = MapPointSize.Scale;
    public string MapPointScaleText => $"{MapPointScale * 100:0}%";
    partial void OnMapPointScaleChanged(double value)
    {
        MapPointSize.Scale = value;
        OnPropertyChanged(nameof(MapPointScaleText));
    }

    // ── Save & Clear ──────────────────────────────────────────────────────────
    // Folder, file name and format for Save & Clear (hamburger menu), and when Auto Save And Clear runs.
    // Persisted through SaveAndClearSettings and read when a save runs.
    public IReadOnlyList<string> SaveFormatOptions { get; } = new[] { "VS1", "VSZ (zipped)" };
    public IReadOnlyList<string> AutoSaveTriggerOptions { get; } = new[] { "After a number of APs", "After a time" };

    [ObservableProperty] private string _saveFolder         = Services.SaveFolder.Describe(SaveAndClearSettings.Resolve().Folder);
    [ObservableProperty] private bool   _isCustomSaveFolder = SaveAndClearSettings.Resolve().UsedChoice;
    [ObservableProperty] private string _saveFileName       = SaveAndClearSettings.FileName;
    [ObservableProperty] private string _selectedSaveFormat =
        SaveAndClearSettings.Format == SaveFileFormat.Vsz ? "VSZ (zipped)" : "VS1";
    [ObservableProperty] private bool   _autoSaveAndClear   = SaveAndClearSettings.AutoEnabled;
    [ObservableProperty] private string _selectedAutoSaveTrigger =
        SaveAndClearSettings.Trigger == AutoSaveTrigger.Time ? "After a time" : "After a number of APs";
    [ObservableProperty] private bool   _isAutoSaveByTime   = SaveAndClearSettings.Trigger == AutoSaveTrigger.Time;
    [ObservableProperty] private string _autoSaveApCount    = SaveAndClearSettings.ApCount.ToString();
    [ObservableProperty] private string _autoSaveMinutes    = SaveAndClearSettings.Minutes.ToString();
    [ObservableProperty] private bool   _uploadSavesToWifiDb = SaveAndClearSettings.UploadToWifiDb;
    [ObservableProperty] private string _saveFolderStatus   = string.Empty;

    public string SaveFileNameExample => SaveAndClearSettings.BuildFileName(DateTime.Now);

    partial void OnSaveFileNameChanged(string value)
    {
        SaveAndClearSettings.FileName = value;
        OnPropertyChanged(nameof(SaveFileNameExample));
    }

    partial void OnSelectedSaveFormatChanged(string value)
    {
        SaveAndClearSettings.Format = value.StartsWith("VSZ") ? SaveFileFormat.Vsz : SaveFileFormat.Vs1;
        OnPropertyChanged(nameof(SaveFileNameExample));
    }

    partial void OnAutoSaveAndClearChanged(bool value) => SaveAndClearSettings.AutoEnabled = value;

    partial void OnSelectedAutoSaveTriggerChanged(string value)
    {
        IsAutoSaveByTime = value == "After a time";
        SaveAndClearSettings.Trigger = IsAutoSaveByTime ? AutoSaveTrigger.Time : AutoSaveTrigger.ApCount;
    }

    partial void OnAutoSaveApCountChanged(string value)
    {
        if (int.TryParse(value, out var n) && n > 0) SaveAndClearSettings.ApCount = n;
    }

    partial void OnAutoSaveMinutesChanged(string value)
    {
        if (int.TryParse(value, out var n) && n > 0) SaveAndClearSettings.Minutes = n;
    }

    partial void OnUploadSavesToWifiDbChanged(bool value) => SaveAndClearSettings.UploadToWifiDb = value;

    [ObservableProperty] private bool _deleteSavesAfterUpload = SaveAndClearSettings.DeleteAfterUpload;
    partial void OnDeleteSavesAfterUploadChanged(bool value) => SaveAndClearSettings.DeleteAfterUpload = value;

    // Saved files still waiting to go to WifiDB (WifiDbUploadQueue)
    [ObservableProperty] private bool   _hasPendingUploads;
    [ObservableProperty] private string _pendingUploadsText = string.Empty;
    [ObservableProperty] private bool   _isRetryingUploads;

    private static WifiDbUploadQueue? UploadQueue =>
        IPlatformApplication.Current?.Services.GetService<WifiDbUploadQueue>();

    private void RefreshPendingUploads()
    {
        var queue = UploadQueue;
        int count = queue?.Count ?? 0;
        HasPendingUploads = count > 0;
        PendingUploadsText = count == 0 ? string.Empty
            : $"{count} saved file{(count == 1 ? "" : "s")} waiting to upload to WifiDB" +
              (queue!.LastError is { } error ? $": {error}" : "");
    }

    private void OnUploadQueueChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshPendingUploads);

    /// <summary>Follow the upload queue while Settings is on screen.</summary>
    public void WatchUploadQueue(bool watch)
    {
        if (UploadQueue is not { } queue) return;
        queue.Changed -= OnUploadQueueChanged;
        if (watch) queue.Changed += OnUploadQueueChanged;
        RefreshPendingUploads();
    }

    [RelayCommand]
    private async Task RetryUploadsAsync()
    {
        if (UploadQueue is not { } queue) return;
        IsRetryingUploads = true;
        try { await queue.ProcessAsync(waitForRunning: true); }
        finally
        {
            IsRetryingUploads = false;
            RefreshPendingUploads();
        }
    }

    [RelayCommand]
    private async Task ClearUploadsAsync()
    {
        if (UploadQueue is not { } queue) return;
        bool ok = await Shell.Current.DisplayAlertAsync("Waiting uploads",
            "Stop trying to upload these files to WifiDB? The files themselves stay in the Save & Clear folder.",
            "Stop uploading", "Cancel");
        if (!ok) return;
        queue.Clear();
        RefreshPendingUploads();
    }

    /// <summary>Choose the Save &amp; Clear folder; refused, with the reason, when the app can't write to it.</summary>
    [RelayCommand]
    private async Task BrowseSaveFolderAsync()
    {
        // On Android this is the system folder picker, written to through the access it grants (SaveFolder)
        var (picked, error) = await Services.SaveFolder.PickAsync(SaveAndClearSettings.Resolve().Folder);
        if (error is not null)
        {
            SaveFolderStatus = $"{error} Keeping {SaveFolder}.";
            return;
        }
        if (picked is null) return;   // cancelled
        SaveAndClearSettings.Folder = picked;
        SaveFolder         = Services.SaveFolder.Describe(picked);
        IsCustomSaveFolder = true;
        SaveFolderStatus   = string.Empty;
    }

    [RelayCommand]
    private void UseDefaultSaveFolder()
    {
        SaveAndClearSettings.ResetFolder();
        SaveFolder         = ExportLocation.DefaultFolder;
        IsCustomSaveFolder = false;
        SaveFolderStatus   = string.Empty;
    }

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

        _selectedFollowZoom = MapFollowSettings.Mode switch
        {
            FollowZoom.Manual      => FollowZoomOptions[1],
            FollowZoom.KeepCurrent => FollowZoomOptions[2],
            _                      => FollowZoomOptions[0],
        };
        _isManualFollowZoom = MapFollowSettings.Mode == FollowZoom.Manual;
        _manualFollowZoom   = MapFollowSettings.ManualZoom.ToString("0.#");

        RefreshComPorts();
        _selectedGpsFormat = GpsFormatOptions[(int)ScanSettings.GpsFormat];
        RefreshManufacturersText();
        _selectedNewApSound = NewApSoundOptions[(int)SoundSettings.NewApMode];
        _selectedSpeakVoice = SpeakVoiceOptions[(int)SoundSettings.Voice];
        _selectedSpeakValue = SpeakValueOptions[(int)SoundSettings.Value];
        if (HasAdapterChoice) _ = RefreshAdaptersAsync();

        // Show the chosen receiver without asking for the Bluetooth permission just to open Settings;
        // Refresh lists the rest
        if (!string.IsNullOrEmpty(GpsSettings.BluetoothAddress))
        {
            var saved = new BluetoothGpsDevice(
                string.IsNullOrEmpty(GpsSettings.BluetoothName) ? GpsSettings.BluetoothAddress : GpsSettings.BluetoothName,
                GpsSettings.BluetoothAddress);
            BluetoothDevices.Add(saved);
            _selectedBluetoothDevice = saved;
        }
    }

    partial void OnSelectedFollowZoomChanged(string value)
    {
        MapFollowSettings.Mode = value == FollowZoomOptions[1] ? FollowZoom.Manual
                               : value == FollowZoomOptions[2] ? FollowZoom.KeepCurrent
                               : FollowZoom.Auto;
        IsManualFollowZoom = MapFollowSettings.Mode == FollowZoom.Manual;
    }

    partial void OnManualFollowZoomChanged(string value)
    {
        if (double.TryParse(value, out var zoom) && zoom is >= 1 and <= 22)
            MapFollowSettings.ManualZoom = zoom;
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
    // Backed by MAUI Preferences so they persist: the account against WifiDbSettings,
    // and the map-data origin against WifiDbTileSources, which is a different host and
    // is read by the history overlays.
    [ObservableProperty] private string _wifiDbUrl     = WifiDbSettings.Url;
    [ObservableProperty] private string _wifiDbUser    = WifiDbSettings.User;
    [ObservableProperty] private string _wifiDbApiKey  = WifiDbSettings.ApiKey;
    [ObservableProperty] private string _wifiDbDataUrl = WifiDbTileSources.DataRoot;
    [ObservableProperty] private string _wifiDbStatus  = string.Empty;

    partial void OnWifiDbUrlChanged(string value)     => WifiDbSettings.Url     = value;
    partial void OnWifiDbUserChanged(string value)    => WifiDbSettings.User    = value;
    partial void OnWifiDbApiKeyChanged(string value)  => WifiDbSettings.ApiKey  = value;
    partial void OnWifiDbDataUrlChanged(string value) => WifiDbTileSources.DataRoot = value;

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

    // ── Manufacturers (ManufacturerDatabase) ──────────────────────────────────
    [ObservableProperty] private string _manufacturersText = string.Empty;
    [ObservableProperty] private bool   _isUpdatingManufacturers;

    private static ManufacturerDatabase? Manufacturers => ManufacturerDatabase.Current;

    private void RefreshManufacturersText()
    {
        if (Manufacturers is not { } db) return;
        var source = db.UpdatedOn is { } on ? $"updated {on:yyyy-MM-dd}" : "the list that came with the app";
        ManufacturersText = db.Count == 0 ? "Loading the manufacturer list…" : $"{db.Count:N0} manufacturers ({source})";
    }

    /// <summary>The original's Extra → Update Manufacturers: download IEEE's current list.</summary>
    [RelayCommand]
    private async Task UpdateManufacturersAsync()
    {
        if (Manufacturers is not { } db) return;
        IsUpdatingManufacturers = true;
        ManufacturersText = "Downloading IEEE's manufacturer list…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            int count = await db.UpdateAsync(http);
            RefreshManufacturersText();
        }
        catch (Exception ex)
        {
            RefreshManufacturersText();
            ManufacturersText += $"\nUpdate failed: {ex.Message}";
        }
        finally
        {
            IsUpdatingManufacturers = false;
        }
    }

    [RelayCommand]
    private static Task OpenMapColorsAsync() => Shell.Current.GoToAsync(nameof(MapColorsPage));

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
