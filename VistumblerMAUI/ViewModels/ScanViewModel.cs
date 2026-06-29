using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Drives the Scan page — starts/stops WiFi scanning, aggregates APs,
/// merges GPS position updates, persists to the database, and (merged
/// from the former standalone AP-list tab) provides search/filter and
/// clear-all over the same AP table, plus a BSSID deep-link entry point
/// for the Map page's "View in AP List" action.
/// </summary>
public partial class ScanViewModel : ObservableObject, IQueryAttributable
{
    private readonly IWiFiScannerService _wifi;
    private readonly IGpsService         _gps;
    private readonly IDatabaseService    _db;
    private readonly ISoundService       _sound;

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _gpsCts;
    private bool _loaded;

    // In-memory AP table (keyed by BSSID for fast upsert) — seeded from the
    // database on first appearance, then kept live by the scan loop.
    private readonly Dictionary<string, AccessPoint> _apMap = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private ObservableCollection<AccessPoint> _accessPoints = new();
    [ObservableProperty] private bool   _isScanning;
    [ObservableProperty] private int    _totalCount;
    [ObservableProperty] private int    _activeCount;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _gpsStatus     = "GPS off";
    [ObservableProperty] private double _loopTimeMs;
    [ObservableProperty] private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => RefreshDisplayedList();

    // Current GPS fix (updated by GPS service)
    private GpsData? _currentGps;

    public ScanViewModel(
        IWiFiScannerService wifi,
        IGpsService         gps,
        IDatabaseService    db,
        ISoundService       sound)
    {
        _wifi  = wifi;
        _gps   = gps;
        _db    = db;
        _sound = sound;

        _wifi.AccessPointsDetected += OnAccessPointsDetected;
        _wifi.ScanError            += OnScanError;
        _gps.GpsDataReceived       += OnGpsData;
    }

    /// <summary>Lets other pages (e.g. the map's "View in AP List" popup action) jump
    /// straight to a specific BSSID via `//ScanPage?bssid=...`.</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("bssid", out var bssid) && bssid is string s && !string.IsNullOrWhiteSpace(s))
            SearchText = s;
    }

    /// <summary>Loads persisted APs from the database once, so the list isn't empty
    /// on a cold start before scanning begins. Safe to call repeatedly — only does
    /// work the first time.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;

        await _db.InitializeAsync();
        foreach (var ap in await _db.GetAllAccessPointsAsync())
        {
            // Loaded history isn't "currently in range" until a scan says otherwise.
            ap.IsActive = false;
            _apMap[ap.Bssid] = ap;
        }

        TotalCount  = _apMap.Count;
        ActiveCount = 0;
        RefreshDisplayedList();
        if (!IsScanning)
            StatusMessage = $"{TotalCount} total APs";
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        await _db.ClearAllAccessPointsAsync();
        _apMap.Clear();
        TotalCount    = 0;
        ActiveCount   = 0;
        RefreshDisplayedList();
        StatusMessage = "Cleared";
    }

    [RelayCommand]
    private async Task ToggleScanAsync()
    {
        if (IsScanning)
            await StopScanAsync();
        else
            await StartScanAsync();
    }

    private async Task StartScanAsync()
    {
        await _db.InitializeAsync();

        _scanCts = new CancellationTokenSource();
        _gpsCts  = new CancellationTokenSource();

        IsScanning    = true;
        StatusMessage = "Scanning…";

        // Start GPS in background
        _ = _gps.StartAsync(_gpsCts.Token);

        // Start WiFi scan loop in background
        _ = _wifi.StartScanningAsync(_scanCts.Token);
    }

    private Task StopScanAsync()
    {
        _scanCts?.Cancel();
        _gpsCts?.Cancel();
        _wifi.StopScanning();
        _gps.Stop();
        IsScanning    = false;
        StatusMessage = $"Stopped — {TotalCount} total APs";
        return Task.CompletedTask;
    }

    private async void OnAccessPointsDetected(object? sender, AccessPointsDetectedEventArgs e)
    {
        var scanTime = DateTime.UtcNow;
        var newCount = 0;

        foreach (var ap in e.AccessPoints)
        {
            // Stamp GPS on the AP if we have a fix
            if (_currentGps != null)
            {
                ap.Latitude  = _currentGps.Latitude;
                ap.Longitude = _currentGps.Longitude;
            }

            bool isNew = !_apMap.ContainsKey(ap.Bssid);
            if (isNew) newCount++;

            // Merge highest signal
            if (_apMap.TryGetValue(ap.Bssid, out var existing))
            {
                if (ap.Signal > existing.HighestSignal)
                    existing.HighestSignal = ap.Signal;
                existing.Signal   = ap.Signal;
                existing.Rssi     = ap.Rssi;
                existing.LastSeen = scanTime;
                existing.IsActive = true;
                ap.ApId = existing.ApId; // carry through DB id
            }
            else
            {
                ap.FirstSeen = ap.LastSeen = scanTime;
                ap.IsActive  = true;
                _apMap[ap.Bssid] = ap;
            }

            await _db.UpsertAccessPointAsync(ap);

            if (ap.Rssi.HasValue)
                await _db.AddSignalHistoryAsync(new Vistumbler.Core.Models.SignalHistory
                {
                    ApId      = ap.ApId,
                    Signal    = ap.Signal ?? 0,
                    Rssi      = ap.Rssi,
                    Latitude  = ap.Latitude,
                    Longitude = ap.Longitude,
                    Timestamp = scanTime
                });
        }

        if (newCount > 0 && _sound.SoundEnabled)
            await _sound.PlayNewNetworkAsync();

        // Update UI on main thread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            TotalCount    = _apMap.Count;
            ActiveCount   = _apMap.Values.Count(a => a.IsActive);
            StatusMessage = $"{ActiveCount} active / {TotalCount} total";
            RefreshDisplayedList();
        });
    }

    /// <summary>Rebuilds the displayed AP list from the live in-memory table,
    /// applying the active search filter (if any).</summary>
    private void RefreshDisplayedList()
    {
        var q = SearchText?.Trim() ?? string.Empty;
        IEnumerable<AccessPoint> source = _apMap.Values;
        if (!string.IsNullOrEmpty(q))
            source = source.Where(a =>
                a.Ssid.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Bssid.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Manufacturer.Contains(q, StringComparison.OrdinalIgnoreCase));

        AccessPoints = new ObservableCollection<AccessPoint>(source
            .OrderByDescending(a => a.IsActive)
            .ThenByDescending(a => a.Signal));
    }

    private void OnGpsData(object? sender, GpsDataReceivedEventArgs e)
    {
        _currentGps = e.GpsData;
        GpsStatus = $"GPS {e.GpsData.Latitude:F5}, {e.GpsData.Longitude:F5}";
        _ = _db.AddGpsDataAsync(e.GpsData);
    }

    private void OnScanError(object? sender, ScanErrorEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
            StatusMessage = $"Error: {e.ErrorMessage}");
    }
}
