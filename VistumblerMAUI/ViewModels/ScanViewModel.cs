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
    private readonly Services.IKeepAliveService _keepAlive;

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _gpsCts;
    private bool _loaded;

    // In-memory AP table (keyed by BSSID for fast upsert) — seeded from the
    // database on first appearance, then kept live by the scan loop.
    private readonly Dictionary<string, AccessPoint> _apMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All known APs this session (live IsActive + coordinates), unfiltered by
    /// the search box — used by the map to plot the current scan's active/dead APs.</summary>
    public IReadOnlyCollection<AccessPoint> AllKnownAps => _apMap.Values;

    [ObservableProperty] private ObservableCollection<AccessPoint> _accessPoints = new();
    [ObservableProperty] private bool   _isScanning;
    [ObservableProperty] private bool   _isGpsEnabled;
    [ObservableProperty] private int    _totalCount;
    [ObservableProperty] private int    _activeCount;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _gpsStatus     = "GPS off";
    [ObservableProperty] private double _loopTimeMs;
    [ObservableProperty] private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => RebuildDisplayedList();

    // APs not re-seen within this many seconds are marked dead (dimmed, still listed).
    private const int DeadAfterSeconds = 30;

    // Scan cadence (ms). Persisted by SettingsViewModel; applied to the scanner on start.
    private const string ScanIntervalKey = "Scan_IntervalMs";

    // ── Sorting ─────────────────────────────────────────────────────────────
    private const string SortOptionKey = "Scan_SortOption";
    private const string SortDescKey   = "Scan_SortDescending";

    /// <summary>Sort fields offered in the Scan-page "Sort by" dropdown.</summary>
    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "Signal", "SSID", "BSSID", "Channel", "Manufacturer", "Security", "Last Seen", "First Seen",
    };

    [ObservableProperty] private string _selectedSortOption;
    [ObservableProperty] private bool   _sortDescending;

    /// <summary>Arrow glyph for the direction toggle button.</summary>
    public string SortDirectionGlyph => SortDescending ? "▼" : "▲";

    partial void OnSelectedSortOptionChanged(string value)
    {
        Preferences.Set(SortOptionKey, value);
        RebuildDisplayedList();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        Preferences.Set(SortDescKey, value);
        OnPropertyChanged(nameof(SortDirectionGlyph));
        RebuildDisplayedList();
    }

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

    // Current GPS fix (updated by GPS service)
    private GpsData? _currentGps;

    public ScanViewModel(
        IWiFiScannerService wifi,
        IGpsService         gps,
        IDatabaseService    db,
        ISoundService       sound,
        Services.IKeepAliveService keepAlive)
    {
        _wifi      = wifi;
        _gps       = gps;
        _db        = db;
        _sound     = sound;
        _keepAlive = keepAlive;

        // Restore the persisted sort choice (set the fields directly so the change
        // handlers don't fire before construction finishes).
        _selectedSortOption = Preferences.Get(SortOptionKey, "Signal");
        _sortDescending     = Preferences.Get(SortDescKey, true);

        _wifi.AccessPointsDetected += OnAccessPointsDetected;
        _wifi.ScanError            += OnScanError;
        _gps.GpsDataReceived       += OnGpsData;
        _gps.GpsError              += OnGpsError;
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
        RebuildDisplayedList();
        if (!IsScanning)
            StatusMessage = $"{TotalCount} total APs";
    }

    /// <summary>Stop capture and clear in-memory state, closing the current session DB so a
    /// new session file can be opened. Used by "New Session" and the Exit actions.</summary>
    public async Task ResetForNewSessionAsync()
    {
        if (IsScanning)   StopScan();
        if (IsGpsEnabled) StopGps();
        _apMap.Clear();
        AccessPoints.Clear();
        _loaded       = false;
        TotalCount    = 0;
        ActiveCount   = 0;
        StatusMessage = "New session";
        await _db.CloseAsync();
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        await _db.ClearAllAccessPointsAsync();
        _apMap.Clear();
        TotalCount    = 0;
        ActiveCount   = 0;
        RebuildDisplayedList();
        StatusMessage = "Cleared";
    }

    // AP scanning and GPS are toggled independently (as in the original Vistumbler's
    // "Scan APs" / "Use GPS" buttons). APs are only logged while scanning is on, at the
    // configured scan interval; positions are only stamped while GPS is on.

    [RelayCommand]
    private async Task ToggleScanAsync()
    {
        if (IsScanning) StopScan();
        else            await StartScanAsync();
    }

    [RelayCommand]
    private void ToggleGps()
    {
        if (IsGpsEnabled) StopGps();
        else              StartGps();
    }

    private async Task StartScanAsync()
    {
        await _db.InitializeAsync();
        _scanCts = new CancellationTokenSource();
        _wifi.ScanIntervalMs = Preferences.Get(ScanIntervalKey, 1000);   // honour the setting
        IsScanning    = true;
        StatusMessage = "Scanning…";
        _ = _wifi.StartScanningAsync(_scanCts.Token);
        UpdateKeepAlive();
    }

    private void StopScan()
    {
        _scanCts?.Cancel();
        _wifi.StopScanning();
        IsScanning    = false;
        StatusMessage = $"Stopped — {TotalCount} total APs";
        UpdateKeepAlive();
    }

    private void StartGps()
    {
        _gpsCts = new CancellationTokenSource();
        IsGpsEnabled = true;
        GpsStatus    = "GPS starting…";
        _ = _gps.StartAsync(_gpsCts.Token);
        UpdateKeepAlive();
    }

    private void StopGps()
    {
        _gpsCts?.Cancel();
        _gps.Stop();
        IsGpsEnabled = false;
        _currentGps  = null;          // don't stamp stale coordinates onto later scans
        GpsStatus    = "GPS off";
        UpdateKeepAlive();
    }

    /// <summary>
    /// While scanning or GPS is active, run the platform keep-alive (Android foreground
    /// service + wakelock) so collection continues with the screen off, and optionally
    /// hold the screen awake (Settings → "Keep screen on while scanning").
    /// </summary>
    private void UpdateKeepAlive()
    {
        bool active = IsScanning || IsGpsEnabled;
        if (active) _keepAlive.Start();
        else        _keepAlive.Stop();

        try
        {
            DeviceDisplay.Current.KeepScreenOn =
                active && Preferences.Get("keep_screen_on", false);
        }
        catch { /* no active window (e.g. during startup) — harmless */ }
    }

    private async void OnAccessPointsDetected(object? sender, AccessPointsDetectedEventArgs e)
    {
        var scanTime = DateTime.UtcNow;
        var gps      = _currentGps;
        var detected = e.AccessPoints;
        int newCount = 0;

        // The canonical AP object to persist for each detection: the merged in-memory row for
        // an existing AP, or the new AP itself. Persisting the *detected* object for an existing
        // AP would write its default (0) FirstSeen/LastSeen over the real values — which is why
        // First Active came up blank.
        var toPersist = new List<AccessPoint>(detected.Count);

        // Apply the model updates on the UI thread. AccessPoint now raises PropertyChanged,
        // and MAUI only refreshes bindings when those events fire on the UI thread — the
        // scanner callback runs on a background thread, so mutating the bound AP objects
        // here (rather than in the old full-collection rebuild) must be marshalled over.
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            foreach (var ap in detected)
            {
                AccessPoint target;

                // Merge into the existing row (updates it in place) or add a new AP.
                if (_apMap.TryGetValue(ap.Bssid, out var existing))
                {
                    // Vistumbler semantics: an AP is plotted where its signal was strongest,
                    // so only re-stamp coordinates when this detection beats the previous
                    // best signal (or the AP has no fix yet). Overwriting on every cycle
                    // dragged all previously-seen APs along to the device's current position.
                    bool strongerSignal =
                        (ap.Signal ?? int.MinValue) > (existing.HighestSignal ?? int.MinValue) ||
                        (ap.Rssi.HasValue && ap.Rssi.Value > (existing.HighestRssi ?? int.MinValue));
                    if ((ap.Signal ?? int.MinValue) > (existing.HighestSignal ?? int.MinValue))
                        existing.HighestSignal = ap.Signal;
                    if (ap.Rssi.HasValue && ap.Rssi.Value > (existing.HighestRssi ?? int.MinValue))
                        existing.HighestRssi = ap.Rssi;
                    existing.Signal   = ap.Signal;
                    existing.Rssi     = ap.Rssi;
                    existing.LastSeen = scanTime;
                    // Heal rows whose FirstSeen was lost (0) by the earlier overwrite bug.
                    if (existing.FirstSeen == default)
                        existing.FirstSeen = scanTime;
                    if (gps != null && (strongerSignal || !existing.Latitude.HasValue))
                    {
                        existing.Latitude  = gps.Latitude;
                        existing.Longitude = gps.Longitude;
                    }
                    existing.IsActive = true;
                    target = existing;
                }
                else
                {
                    ap.FirstSeen     = ap.LastSeen = scanTime;
                    ap.HighestSignal = ap.Signal;
                    ap.HighestRssi   = ap.Rssi;
                    ap.IsActive      = true;
                    if (gps != null)
                    {
                        ap.Latitude  = gps.Latitude;
                        ap.Longitude = gps.Longitude;
                    }
                    _apMap[ap.Bssid] = ap;
                    newCount++;
                    target = ap;
                }

                toPersist.Add(target);
            }

            // Mark APs not seen within the timeout as dead (dimmed in the list, still shown).
            var deadCutoff = scanTime.AddSeconds(-DeadAfterSeconds);
            foreach (var existing in _apMap.Values)
                if (existing.IsActive && existing.LastSeen < deadCutoff)
                    existing.IsActive = false;

            TotalCount    = _apMap.Count;
            ActiveCount   = _apMap.Values.Count(a => a.IsActive);
            StatusMessage = $"{ActiveCount} active / {TotalCount} total";
            MergeScanResults();
        });

        // Persist the whole cycle in one transaction (GPS + AP upserts + HIST samples +
        // history-link maintenance) — the VistumblerMDB structure, batched for speed.
        await _db.SaveScanCycleAsync(toPersist, gps, scanTime);

        if (newCount > 0 && _sound.SoundEnabled)
            await _sound.PlayNewNetworkAsync();
    }

    private bool MatchesSearch(AccessPoint a, string q) =>
        a.Ssid.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        a.Bssid.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        a.Manufacturer.Contains(q, StringComparison.OrdinalIgnoreCase);

    /// <summary>Comparison for the current sort field + direction, with a stable BSSID tiebreak.</summary>
    private Comparison<AccessPoint> CurrentComparison()
    {
        int dir = SortDescending ? -1 : 1;
        Comparison<AccessPoint> primary = SelectedSortOption switch
        {
            "SSID"         => (a, b) => string.Compare(a.Ssid, b.Ssid, StringComparison.OrdinalIgnoreCase),
            "BSSID"        => (a, b) => string.Compare(a.Bssid, b.Bssid, StringComparison.OrdinalIgnoreCase),
            "Channel"      => (a, b) => a.Channel.CompareTo(b.Channel),
            "Manufacturer" => (a, b) => string.Compare(a.Manufacturer, b.Manufacturer, StringComparison.OrdinalIgnoreCase),
            "Security"     => (a, b) => ((int)a.Authentication).CompareTo((int)b.Authentication),
            "Last Seen"    => (a, b) => a.LastSeen.CompareTo(b.LastSeen),
            "First Seen"   => (a, b) => a.FirstSeen.CompareTo(b.FirstSeen),
            _              => (a, b) => (a.Signal ?? int.MinValue).CompareTo(b.Signal ?? int.MinValue),
        };
        return (a, b) =>
        {
            int c = dir * primary(a, b);
            return c != 0 ? c : string.Compare(a.Bssid, b.Bssid, StringComparison.OrdinalIgnoreCase);
        };
    }

    /// <summary>
    /// Full rebuild of the displayed list order — used only on explicit changes (load, clear,
    /// search text, sort field/direction), where reordering (and any scroll change) is expected.
    /// Reconciles the existing ObservableCollection in place (remove / move / insert) rather than
    /// replacing it, keeping item references stable.
    /// </summary>
    private void RebuildDisplayedList()
    {
        var q = SearchText?.Trim() ?? string.Empty;
        IEnumerable<AccessPoint> source = _apMap.Values;
        if (!string.IsNullOrEmpty(q))
            source = source.Where(a => MatchesSearch(a, q));

        var desired = source.ToList();
        desired.Sort(CurrentComparison());

        var wanted = new HashSet<AccessPoint>(desired);
        for (int i = AccessPoints.Count - 1; i >= 0; i--)
            if (!wanted.Contains(AccessPoints[i]))
                AccessPoints.RemoveAt(i);

        for (int i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i < AccessPoints.Count && ReferenceEquals(AccessPoints[i], item))
                continue;

            int existing = AccessPoints.IndexOf(item);
            if (existing >= 0)
                AccessPoints.Move(existing, i);
            else
                AccessPoints.Insert(i, item);
        }
    }

    /// <summary>
    /// Incremental update after a scan cycle — mirrors the original Vistumbler.au3 behaviour of
    /// updating existing rows in place (by AP) and only inserting genuinely-new APs. Existing rows
    /// refresh their values via PropertyChanged with no collection change, and their order is left
    /// untouched, so the CollectionView keeps its scroll position (no jump to top on refresh).
    /// New APs are inserted at their sorted position. A re-sort only happens on explicit user
    /// action (see RebuildDisplayedList).
    /// </summary>
    private void MergeScanResults()
    {
        var q = SearchText?.Trim() ?? string.Empty;
        var shown = new HashSet<AccessPoint>(AccessPoints);
        var cmp = CurrentComparison();

        foreach (var ap in _apMap.Values)
        {
            if (shown.Contains(ap)) continue;
            if (!string.IsNullOrEmpty(q) && !MatchesSearch(ap, q)) continue;

            int idx = 0;
            while (idx < AccessPoints.Count && cmp(AccessPoints[idx], ap) <= 0) idx++;
            AccessPoints.Insert(idx, ap);
        }
    }

    private void OnGpsData(object? sender, GpsDataReceivedEventArgs e)
    {
        // Just track the latest fix. GPS rows are written once per scan cycle in
        // OnAccessPointsDetected and linked to that cycle's HIST samples (rather than
        // logging every raw fix here, which produced GPS rows nothing referenced).
        _currentGps = e.GpsData;
        var text = $"GPS {e.GpsData.Latitude:F5}, {e.GpsData.Longitude:F5}";
        // The GPS callback runs on a background thread; the status label only refreshes
        // when the bound property changes on the UI thread.
        MainThread.BeginInvokeOnMainThread(() => GpsStatus = text);
    }

    private void OnGpsError(object? sender, GpsErrorEventArgs e)
    {
        // Surface why GPS didn't start (e.g. permission denied / location off) instead of
        // leaving the status stuck on "GPS starting…".
        if (!IsGpsEnabled) return;
        MainThread.BeginInvokeOnMainThread(() => GpsStatus = $"GPS: {e.ErrorMessage}");
    }

    private void OnScanError(object? sender, ScanErrorEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
            StatusMessage = $"Error: {e.ErrorMessage}");
    }
}
