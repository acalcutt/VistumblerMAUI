using VistumblerMAUI.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using VistumblerMAUI.Controls;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Backs the AP details page: loads a single access point (by BSSID) plus its recorded
/// signal history, and feeds the signal-over-time graph. Reached from the Scan list.
/// While the page is visible the graph and AP fields refresh live every 2 seconds.
/// </summary>
public partial class ApDetailsViewModel : ObservableObject, IQueryAttributable
{
    private readonly IDatabaseService _db;

    private string _bssid = string.Empty;
    private CancellationTokenSource? _liveCts;

    private const int LiveRefreshIntervalMs = 2000;

    private readonly ISoundService _sound;
    private readonly ScanViewModel _scan;
    private DateTime _lastSpoken = DateTime.MinValue;
    private int _tick;

    public ApDetailsViewModel(IDatabaseService db, ISoundService sound, ScanViewModel scan)
    {
        _db    = db;
        _sound = sound;
        _scan  = scan;
    }

    /// <summary>
    /// The original's "Speak Signal": while scanning, says this AP's signal every few seconds (Settings → Sound
    /// sets the voice and how often), so it can be followed without looking at the screen.
    /// </summary>
    [ObservableProperty] private bool _speakSignal = Services.SoundSettings.SpeakSignal;
    partial void OnSpeakSignalChanged(bool value) => Services.SoundSettings.SpeakSignal = value;

    private void SpeakIfDue(string bssid)
    {
        if (!SpeakSignal || !_scan.IsScanning) return;
        if (DateTime.UtcNow - _lastSpoken < TimeSpan.FromMilliseconds(Services.SoundSettings.SpeakIntervalMs)) return;
        _lastSpoken = DateTime.UtcNow;
        // Out of range counts as 0, as in the original
        var live = _scan.AllKnownAps.FirstOrDefault(a => string.Equals(a.Bssid, bssid, StringComparison.OrdinalIgnoreCase));
        int signal = live is { IsActive: true } ? live.Signal ?? 0 : 0;
        _ = _sound.SpeakSignalAsync(signal, live is { IsActive: true } ? live.Rssi : null);
    }

    [ObservableProperty] private AccessPoint? _ap;
    [ObservableProperty] private string _title = Loc.T("ApDetails_Title");
    [ObservableProperty] private string _historyStatus = string.Empty;

    /// <summary>Drawable for the signal graph; the page binds it to a GraphicsView.</summary>
    public SignalGraphDrawable Graph { get; } = new();

    /// <summary>Raised after the graph data changes so the page can invalidate the GraphicsView.</summary>
    public event Action? GraphUpdated;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("bssid", out var raw) || raw is not string bssid || string.IsNullOrWhiteSpace(bssid))
            return;
        _bssid = Uri.UnescapeDataString(bssid);
        await _db.InitializeAsync();
        await RefreshAsync(_bssid);
    }

    /// <summary>Begin the live-refresh loop. Called by the page's OnAppearing.</summary>
    public void StartLiveUpdates()
    {
        if (string.IsNullOrWhiteSpace(_bssid)) return;
        StopLiveUpdates();
        _liveCts = new CancellationTokenSource();
        _ = LiveLoopAsync(_bssid, _liveCts.Token);
    }

    /// <summary>Stop the live-refresh loop. Called by the page's OnDisappearing.</summary>
    public void StopLiveUpdates()
    {
        _liveCts?.Cancel();
        _liveCts?.Dispose();
        _liveCts = null;
    }

    private async Task LiveLoopAsync(string bssid, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Check every second so a speaking interval of 1 s works; the page itself refreshes every 2 s
                await Task.Delay(1000, ct);
                if (ct.IsCancellationRequested) break;
                SpeakIfDue(bssid);
                if ((++_tick * 1000) % LiveRefreshIntervalMs == 0)
                    await RefreshAsync(bssid);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshAsync(string bssid)
    {
        var ap = await _db.GetAccessPointByBssidAsync(bssid);
        if (ap is null)
        {
            Title = bssid;
            HistoryStatus = Loc.T("ApDetails_NotFound");
            return;
        }

        Ap    = ap;
        Title = string.IsNullOrWhiteSpace(ap.Ssid) ? bssid : ap.Ssid;

        var history = (await _db.GetSignalHistoryAsync(ap.ApId))
            .OrderBy(h => h.Timestamp)
            .ToList();

        Graph.SetPoints(history.Select(h => h.Signal).ToList());
        HistoryStatus = history.Count == 0
            ? Loc.T("ApDetails_NoHistory")
            : Loc.T("ApDetails_Samples", history.Count);
        GraphUpdated?.Invoke();
    }

    /// <summary>
    /// The original's Copy (Edit menu, or right-click on an AP): copies all of this AP's details, or one field,
    /// to the clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyAsync()
    {
        if (Ap is not { } ap) return;
        var choices = new List<(string Title, string Text)>
        {
            (Loc.T("ApDetails_AllDetails"), DetailsText(ap)),
            ("BSSID", ap.Bssid),
        };
        if (!string.IsNullOrEmpty(ap.Ssid)) choices.Add(("SSID", ap.Ssid));
        if (ap.HasGps) choices.Add((Loc.T("ApDetails_GpsPosition"), ap.GpsText));

        var choice = await Shell.Current.DisplayActionSheetAsync("Copy", "Cancel", null,
            choices.Select(c => c.Title).ToArray());
        var text = choices.FirstOrDefault(c => c.Title == choice).Text;
        if (text is null) return;
        await Clipboard.Default.SetTextAsync(text);
        HistoryStatus = Loc.T("ApDetails_Copied", choice!);
    }

    /// <summary>The original's KML "Selected AP" maps (signal map, signal circle, range circle), on the Map tab.</summary>
    [RelayCommand]
    private Task ShowOnMapAsync() =>
        Ap is { } ap ? Shell.Current.GoToAsync($"//MapPage?apmap={Uri.EscapeDataString(ap.Bssid)}") : Task.CompletedTask;

    /// <summary>The original's Locate in WifiDB: what WifiDB knows about this AP, with a link to its page there.</summary>
    [RelayCommand]
    private async Task LocateInWifiDbAsync()
    {
        if (Ap is not { } ap) return;
        HistoryStatus = Loc.T("ApDetails_SearchingWifiDb");
        IReadOnlyList<Services.WifiDbAp> found;
        try
        {
            found = await Services.WifiDbLookup.FindAsync(ap.Bssid);
        }
        catch (Exception ex)
        {
            HistoryStatus = string.Empty;
            await Shell.Current.DisplayAlertAsync("WifiDB", Loc.T("ApDetails_SearchFailed", ex.Message), Loc.T("Common_Ok"));
            return;
        }
        HistoryStatus = string.Empty;

        // The search matches partial MACs too, so prefer the exact BSSID
        var match = found.FirstOrDefault(a => string.Equals(a.Mac, ap.Bssid, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            await Shell.Current.DisplayAlertAsync("WifiDB", Loc.T("ApDetails_NotInWifiDb", ap.Bssid), Loc.T("Common_Ok"));
            return;
        }

        var lines = new List<string>
        {
            $"SSID: {match.Ssid}",
            Loc.T("ApDetails_Security", match.Security),
            Loc.T("ApDetails_Channel", match.Channel),
            Loc.T("ApDetails_FirstSeen", match.FirstSeen),
            Loc.T("ApDetails_LastSeen", match.LastSeen),
        };
        if (match.HighSignal is not null) lines.Add(Loc.T("ApDetails_BestSignal", match.HighSignal));
        if (match.Latitude is not null && match.Longitude is not null &&
            double.TryParse(match.Latitude, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(match.Longitude, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon) &&
            (lat != 0 || lon != 0))
            lines.Add(Loc.T("ApDetails_Position", GpsFormatter.ToText(lat, lon)));

        if (await Shell.Current.DisplayAlertAsync($"WifiDB: {match.Mac}", string.Join(Environment.NewLine, lines),
                Loc.T("ApDetails_OpenInWifiDb"), Loc.T("Common_Close")))
        {
            try { await Launcher.Default.OpenAsync(new Uri(match.PageUrl)); }
            catch { /* no browser */ }
        }
    }

    private static string DetailsText(AccessPoint ap)
    {
        var lines = new List<string>
        {
            $"SSID: {ap.Ssid}",
            $"BSSID: {ap.Bssid}",
            Loc.T("Map_Manufacturer", ap.Manufacturer),
            Loc.T("ApDetails_Signal", ap.Signal) + (ap.Rssi.HasValue ? $" ({ap.Rssi} dBm)" : ""),
            Loc.T("ApDetails_ChannelMhz", ap.Channel, ap.FrequencyMhz),
            Loc.T("ApDetails_RadioLine", ap.RadioType),
            Loc.T("ApDetails_Security", ap.AuthText + " / " + ap.EncryptionText),
            Loc.T("ApDetails_FirstActiveLine", ap.FirstSeenText),
            Loc.T("ApDetails_LastActiveLine", ap.LastSeenText),
        };
        if (ap.HasGps) lines.Add($"GPS: {ap.GpsText}");
        return string.Join(Environment.NewLine, lines);
    }
}
