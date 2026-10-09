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
    private readonly IExportService      _export;
    private readonly Services.WifiDbUploadQueue _uploadQueue;
    private readonly Services.ManufacturerDatabase _manufacturers;

    // Serializes a scan cycle's database write with clearing the session (Clear All, Save & Clear), so a
    // clear never lands in the middle of a write. Each clear bumps _clearGeneration (on the UI thread); a
    // cycle merged into memory before the clear finds it changed and drops its write instead of putting
    // the cleared APs back into the database.
    private readonly SemaphoreSlim _persistLock = new(1, 1);
    private int _clearGeneration;
    private bool _saveAndClearRunning;
    private DateTime _lastSaveAndClear = DateTime.UtcNow;

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _gpsCts;
    private bool _loaded;

    // In-memory AP table (keyed by BSSID for fast upsert) — seeded from the
    // database on first appearance, then kept live by the scan loop.
    private readonly Dictionary<string, AccessPoint> _apMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All known APs this session (live IsActive + coordinates), unfiltered by
    /// the search box — used by the map to plot the current scan's active/dead APs.</summary>
    public IReadOnlyCollection<AccessPoint> AllKnownAps => _apMap.Values;

    /// <summary>
    /// Raised on the UI thread after each scan cycle is merged, with the APs that scan heard (their merged rows,
    /// carrying this scan's signal). The site survey takes its readings from it.
    /// </summary>
    public event EventHandler<IReadOnlyList<AccessPoint>>? ScanCycleMerged;

    [ObservableProperty] private ObservableCollection<AccessPoint> _accessPoints = new();
    [ObservableProperty] private bool   _isScanning;
    [ObservableProperty] private bool   _isGpsEnabled;
    [ObservableProperty] private int    _totalCount;
    [ObservableProperty] private int    _activeCount;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _gpsStatus     = "GPS off";
    [ObservableProperty] private double _loopTimeMs;
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>The filter in use (Filters page), shown on the Scan page's Filter button.</summary>
    [ObservableProperty] private string _filterLabel = Services.ApFilterStore.Active?.Name ?? "No filter";

    [RelayCommand]
    private static Task OpenFiltersAsync() => Shell.Current.GoToAsync(nameof(Views.FiltersPage));

    partial void OnSearchTextChanged(string value)
    {
        RebuildDisplayedList();
        RebuildRadioList();
    }

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
        Services.IKeepAliveService keepAlive,
        IExportService      export,
        Services.WifiDbUploadQueue uploadQueue,
        Services.ManufacturerDatabase manufacturers,
        IRadioScannerService radio)
    {
        _wifi      = wifi;
        _gps       = gps;
        _db        = db;
        _sound     = sound;
        _keepAlive = keepAlive;
        _export    = export;
        _uploadQueue = uploadQueue;
        _manufacturers = manufacturers;
        InitRadio(radio);   // cell towers and Bluetooth (ScanViewModel.Radio.cs)
        _manufacturers.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshManufacturers);
        Services.ApFilterStore.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            FilterLabel = Services.ApFilterStore.Active?.Name ?? "No filter";
            RebuildDisplayedList();
        });

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
        _manufacturers.FillMissing(_apMap.Values);   // sessions saved before the lookup existed
        await LoadRadioAsync();

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
        ClearRadioInMemory();
        _loaded       = false;
        TotalCount    = 0;
        ActiveCount   = 0;
        StatusMessage = "New session";
        await _db.CloseAsync();
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        await _persistLock.WaitAsync();
        try
        {
            await _db.ClearAllAccessPointsAsync();
            await MainThread.InvokeOnMainThreadAsync(() => ClearInMemory("Cleared"));
        }
        finally
        {
            _persistLock.Release();
        }
    }

    /// <summary>Empties the in-memory AP list after the database has been cleared. UI thread only.</summary>
    private void ClearInMemory(string status)
    {
        _clearGeneration++;
        _apMap.Clear();
        ClearRadioInMemory();
        TotalCount    = 0;
        ActiveCount   = 0;
        RebuildDisplayedList();
        StatusMessage = status;
    }

    /// <param name="Path">The file written, or null when nothing was saved.</param>
    /// <param name="Message">What happened, for the status line or an alert.</param>
    public record SaveAndClearResult(string? Path, int Count, string Message);

    /// <summary>
    /// The original Vistumbler's Save &amp; Clear: write the session to a VS1/VSZ file in the Save &amp; Clear
    /// folder (Settings → Save &amp; Clear), then clear the AP list, which keeps scanning. Nothing is cleared
    /// unless the file was written. Uploads the file to WifiDB afterwards when that is turned on.
    /// </summary>
    public async Task<SaveAndClearResult> SaveAndClearAsync(bool waitForUpload = true)
    {
        if (_saveAndClearRunning) return new(null, 0, "Save & Clear is already running");
        _saveAndClearRunning = true;
        try
        {
            // The saved file's location: a path, or a content:// URI in a folder picked on Android
            string path;
            int count = 0;
            var fileName = Services.SaveAndClearSettings.BuildFileName(DateTime.Now);
            await _persistLock.WaitAsync();
            try
            {
                var (folder, _) = Services.SaveAndClearSettings.Resolve();
                var saved = await Services.SaveFolder.SaveAsync(folder, fileName, async p =>
                    count = await Services.SessionFileExporter.ExportAsync(_db, _export, p, Services.SaveAndClearSettings.Format));
                if (saved is null || count == 0)
                    return new(null, 0, "No access points to save");
                path = saved;

                await _db.ClearAllAccessPointsAsync();
                _lastSaveAndClear = DateTime.UtcNow;
                await MainThread.InvokeOnMainThreadAsync(() =>
                    ClearInMemory($"Saved {count} APs to {fileName} and cleared"));
            }
            catch (Exception ex)
            {
                // The list is only cleared after the file is written, so a failure keeps everything
                var failed = $"Save failed, list not cleared: {ex.Message}";
                await MainThread.InvokeOnMainThreadAsync(() => StatusMessage = failed);
                return new(null, 0, failed);
            }
            finally
            {
                _persistLock.Release();
            }

            var message = $"Saved {count} APs to {Services.SaveFolder.Describe(path)}";
            if (Services.SaveAndClearSettings.UploadToWifiDb)
            {
                // Queued, so a file that can't go now (offline, WifiDB down, no account yet) is retried later
                _uploadQueue.Enqueue(path, Path.GetFileNameWithoutExtension(fileName), "Saved by VistumblerMAUI Save & Clear");
                var upload = UploadQueuedAsync(path);
                if (waitForUpload)
                    message += $"\n{await upload}";
                else
                    _ = upload;   // an automatic save doesn't wait; the status line reports the outcome
            }
            return new(path, count, message);
        }
        finally
        {
            _saveAndClearRunning = false;
        }
    }

    /// <summary>Works through the WifiDB upload queue and describes what happened to <paramref name="path"/>.</summary>
    private async Task<string> UploadQueuedAsync(string path)
    {
        await MainThread.InvokeOnMainThreadAsync(() => StatusMessage = "Uploading to WifiDB…");
        await _uploadQueue.ProcessAsync(waitForRunning: true);
        var outcome = _uploadQueue.Contains(path)
            ? $"Not uploaded to WifiDB yet ({_uploadQueue.ErrorFor(path) ?? "queued"}); it will be retried"
            : "Uploaded to WifiDB";
        await MainThread.InvokeOnMainThreadAsync(() => StatusMessage = outcome);
        return outcome;
    }

    /// <summary>Runs Save &amp; Clear when Auto Save And Clear is on and its AP count or time is reached.</summary>
    private async Task AutoSaveAndClearIfDueAsync()
    {
        if (!IsScanning || _saveAndClearRunning || TotalCount == 0 || !Services.SaveAndClearSettings.AutoEnabled)
            return;
        bool due = Services.SaveAndClearSettings.Trigger == Services.AutoSaveTrigger.ApCount
            ? TotalCount >= Services.SaveAndClearSettings.ApCount
            : DateTime.UtcNow - _lastSaveAndClear >= TimeSpan.FromMinutes(Services.SaveAndClearSettings.Minutes);
        if (due)
            await SaveAndClearAsync(waitForUpload: false);
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

    /// <summary>
    /// Looks every AP's manufacturer up again, once the list has loaded or been updated (Settings → Data →
    /// Update manufacturers). A name the list doesn't have, e.g. one from an imported file, is kept. UI thread.
    /// </summary>
    private void RefreshManufacturers()
    {
        foreach (var ap in _apMap.Values)
            if (_manufacturers.Lookup(ap.Bssid) is { Length: > 0 } name)
                ap.Manufacturer = name;
    }

    /// <summary>Starts scanning and/or GPS when Settings → Scanning says to when the app opens.</summary>
    public async Task StartOnLaunchAsync()
    {
        if (Services.ScanSettings.ScanOnLaunch && !IsScanning)
        {
            await LoadCommand.ExecuteAsync(null);   // list the session's saved APs before new ones arrive
            await StartScanAsync();
        }
        if (Services.ScanSettings.GpsOnLaunch && !IsGpsEnabled)
            await StartGpsAsync();
    }

    [RelayCommand]
    private async Task ToggleGpsAsync()
    {
        if (IsGpsEnabled) StopGps();
        else              await StartGpsAsync();
    }

    /// <summary>
    /// Android needs location permission both for Wi-Fi scan results and for the location-type
    /// foreground service that UpdateKeepAlive starts. Ask before starting either: starting that
    /// service while the permission dialog is still open throws a SecurityException and kills the app.
    /// </summary>
    private static async Task<bool> EnsureLocationPermissionAsync()
    {
        if (!OperatingSystem.IsAndroid()) return true;
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        return status == PermissionStatus.Granted;
    }

    private async Task StartScanAsync()
    {
        if (!await EnsureLocationPermissionAsync())
        {
            StatusMessage = "Location permission is needed to scan for Wi-Fi";
            return;
        }
        await _db.InitializeAsync();
        _scanCts = new CancellationTokenSource();
        _wifi.ScanIntervalMs = Preferences.Get(ScanIntervalKey, 1000);   // honour the setting
        IsScanning    = true;
        StatusMessage = "Scanning…";
        _lastSaveAndClear = DateTime.UtcNow;   // Auto Save And Clear's timer counts scanning time
        _ = _wifi.StartScanningAsync(_scanCts.Token);
        StartRadio();
        UpdateKeepAlive();
    }

    private void StopScan()
    {
        _scanCts?.Cancel();
        _wifi.StopScanning();
        StopRadio();
        IsScanning    = false;
        StatusMessage = $"Stopped — {TotalCount} total APs";
        UpdateKeepAlive();
    }

    private async Task StartGpsAsync()
    {
        if (!await EnsureLocationPermissionAsync())
        {
            GpsStatus = "GPS: location permission denied";
            return;
        }
        _gpsCts = new CancellationTokenSource();
        IsGpsEnabled = true;
        GpsStatus    = "GPS starting…";
        _ = _gps.StartAsync(_gpsCts.Token);
        UpdateKeepAlive();

        if (_gpsWatchdog is null && Application.Current?.Dispatcher is { } dispatcher)
        {
            _gpsWatchdog = dispatcher.CreateTimer();
            _gpsWatchdog.Interval = TimeSpan.FromSeconds(1);
            _gpsWatchdog.Tick += (_, _) => CheckGpsFixAge();
        }
        _gpsWatchdog?.Start();
    }

    private void StopGps()
    {
        _gpsWatchdog?.Stop();
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
        var newSignals = new List<int>();   // for the new-AP sound

        // The canonical AP object to persist for each detection: the merged in-memory row for
        // an existing AP, or the new AP itself. Persisting the *detected* object for an existing
        // AP would write its default (0) FirstSeen/LastSeen over the real values — which is why
        // First Active came up blank.
        var toPersist = new List<AccessPoint>(detected.Count);
        int generation = 0;

        // Apply the model updates on the UI thread. AccessPoint now raises PropertyChanged,
        // and MAUI only refreshes bindings when those events fire on the UI thread — the
        // scanner callback runs on a background thread, so mutating the bound AP objects
        // here (rather than in the old full-collection rebuild) must be marshalled over.
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            generation = _clearGeneration;
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
                    if (string.IsNullOrEmpty(existing.Manufacturer))
                        existing.Manufacturer = _manufacturers.Lookup(existing.Bssid);
                    target = existing;
                }
                else
                {
                    ap.FirstSeen     = ap.LastSeen = scanTime;
                    ap.HighestSignal = ap.Signal;
                    ap.HighestRssi   = ap.Rssi;
                    ap.IsActive      = true;
                    ap.Manufacturer  = _manufacturers.Lookup(ap.Bssid);
                    if (gps != null)
                    {
                        ap.Latitude  = gps.Latitude;
                        ap.Longitude = gps.Longitude;
                    }
                    _apMap[ap.Bssid] = ap;
                    newCount++;
                    newSignals.Add(ap.Signal ?? 0);
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
            ScanCycleMerged?.Invoke(this, toPersist);
        });

        // Persist the whole cycle in one transaction (GPS + AP upserts + HIST samples +
        // history-link maintenance) — the VistumblerMDB structure, batched for speed.
        // This handler is async void (event handler): an unhandled exception here kills
        // the whole app. A transient write failure (observed in the field: "attempt to
        // write a readonly database" after backgrounding, i.e. a stale/bad connection)
        // must instead drop this cycle's persist — the in-memory state is already
        // updated and the next cycle re-persists everything current. Closing the
        // connection makes the next cycle's InitializeAsync reopen a fresh one.
        await _persistLock.WaitAsync();
        try
        {
            // Cleared (Clear All / Save & Clear) after this cycle was merged: its APs are gone from the
            // list, so writing them would put them back in the database only.
            if (generation != _clearGeneration) return;
            if (toPersist.Count > 0)
                await _db.SaveScanCycleAsync(toPersist, gps, scanTime);
            else if (gps is not null && Services.ScanSettings.SaveGpsWithoutAps)
                await AddGpsPointAsync(gps, scanTime);   // the original's "Save all GPS data": no APs this scan
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScanVM] SaveScanCycleAsync failed (cycle dropped): {ex.Message}");
            StatusMessage = "DB write failed — retrying next cycle";
            try { await _db.CloseAsync(); } catch { /* reopened on next cycle */ }
            return;
        }
        finally
        {
            _persistLock.Release();
        }

        if (newCount > 0 && _sound.SoundEnabled)  // not awaited by the next scan: sounds play alongside
            _ = _sound.PlayNewNetworksAsync(newSignals);

        await AutoSaveAndClearIfDueAsync();
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
        if (Services.ApFilterStore.Active is { } filter)
            source = source.Where(filter.Matches);

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
        SyncGroups();
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
        var filter = Services.ApFilterStore.Active;

        // Like the original's _FilterRemoveNonMatchingInList: drop rows that stopped matching (e.g. went dead
        // under an "active only" filter); the loop below adds ones that started to
        if (filter is not null)
            for (int i = AccessPoints.Count - 1; i >= 0; i--)
                if (!filter.Matches(AccessPoints[i])) { shown.Remove(AccessPoints[i]); AccessPoints.RemoveAt(i); }

        foreach (var ap in _apMap.Values)
        {
            if (shown.Contains(ap)) continue;
            if (!string.IsNullOrEmpty(q) && !MatchesSearch(ap, q)) continue;
            if (filter is not null && !filter.Matches(ap)) continue;

            int idx = 0;
            while (idx < AccessPoints.Count && cmp(AccessPoints[idx], ap) <= 0) idx++;
            AccessPoints.Insert(idx, ap);
        }
        SyncGroups();
    }

    private DateTime _lastGpsPointSaved = DateTime.MinValue;

    /// <summary>Records one GPS point on its own (no APs), under the same lock as scan cycles and clears.</summary>
    private async Task SaveGpsPointAsync(GpsData gps)
    {
        await _persistLock.WaitAsync();
        try { await AddGpsPointAsync(gps, DateTime.UtcNow); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ScanVM] GPS point not saved: {ex.Message}"); }
        finally { _persistLock.Release(); }
    }

    // Stamped with the time it was recorded, as scan cycles are: a stationary phone repeats one fix timestamp,
    // which would make every point the same. Callers hold _persistLock.
    private async Task AddGpsPointAsync(GpsData gps, DateTime when)
    {
        await _db.InitializeAsync();
        await _db.AddGpsDataAsync(new GpsData
        {
            Latitude = gps.Latitude, Longitude = gps.Longitude, Altitude = gps.Altitude,
            NumberOfSatellites = gps.NumberOfSatellites, HorizontalDilution = gps.HorizontalDilution,
            Accuracy = gps.Accuracy, SpeedKnots = gps.SpeedKnots, TrackAngle = gps.TrackAngle,
            Quality = gps.Quality, Timestamp = when,
        });
    }

    // When the last fix arrived, for Settings → GPS "Reset position when the receiver has no fix"
    private DateTime _lastFixUtc;
    private IDispatcherTimer? _gpsWatchdog;

    /// <summary>
    /// Clears the position once an external receiver has sent no fix for GpsSettings.NoFixTimeout, like the
    /// original's "Reset GPS position when no GPGGA data is received", so APs stop getting its last position.
    /// Not applied to the phone's own GPS, which can go quiet while standing still.
    /// </summary>
    private void CheckGpsFixAge()
    {
        if (_currentGps is null || !Services.GpsSettings.ResetPositionWhenNoFix || !Services.GpsSettings.IsExternalReceiver)
            return;
        if (DateTime.UtcNow - _lastFixUtc <= Services.GpsSettings.NoFixTimeout) return;
        _currentGps = null;
        GpsStatus = $"GPS: no fix for {Services.GpsSettings.NoFixTimeout.TotalSeconds:0} s, position cleared";
    }

    private void OnGpsData(object? sender, GpsDataReceivedEventArgs e)
    {
        // Just track the latest fix. GPS rows are written once per scan cycle in
        // OnAccessPointsDetected and linked to that cycle's HIST samples (rather than
        // logging every raw fix here, which produced GPS rows nothing referenced).
        _currentGps = e.GpsData;
        _lastFixUtc = DateTime.UtcNow;

        // The original's "Save all GPS data": with GPS on but no scan running, keep recording the track,
        // at the scan interval (scans record GPS themselves when they run)
        if (!IsScanning && IsGpsEnabled && Services.ScanSettings.SaveGpsWithoutAps &&
            DateTime.UtcNow - _lastGpsPointSaved >= TimeSpan.FromMilliseconds(Math.Max(1000, Preferences.Get(ScanIntervalKey, 1000))))
        {
            _lastGpsPointSaved = DateTime.UtcNow;
            _ = SaveGpsPointAsync(e.GpsData);
        }
        var text = $"GPS {GpsFormatter.ToText(e.GpsData.Latitude, e.GpsData.Longitude)}";
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

        // The original's error sound when GPS drops; at most every 30 s, since a receiver can keep retrying
        if (DateTime.UtcNow - _lastGpsErrorSound >= TimeSpan.FromSeconds(30))
        {
            _lastGpsErrorSound = DateTime.UtcNow;
            _ = _sound.PlayErrorAsync();
        }
    }

    private DateTime _lastGpsErrorSound = DateTime.MinValue;

    private void OnScanError(object? sender, ScanErrorEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
            StatusMessage = $"Error: {e.ErrorMessage}");
    }
}
