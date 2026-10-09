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
        _ = _sound.SpeakSignalAsync(signal);
    }

    [ObservableProperty] private AccessPoint? _ap;
    [ObservableProperty] private string _title = "AP Details";
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
            HistoryStatus = "AP not found.";
            return;
        }

        Ap    = ap;
        Title = string.IsNullOrWhiteSpace(ap.Ssid) ? bssid : ap.Ssid;

        var history = (await _db.GetSignalHistoryAsync(ap.ApId))
            .OrderBy(h => h.Timestamp)
            .ToList();

        Graph.SetPoints(history.Select(h => h.Signal).ToList());
        HistoryStatus = history.Count == 0
            ? "No signal history yet."
            : $"{history.Count} signal samples";
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
            ("All details", DetailsText(ap)),
            ("BSSID", ap.Bssid),
        };
        if (!string.IsNullOrEmpty(ap.Ssid)) choices.Add(("SSID", ap.Ssid));
        if (ap.HasGps) choices.Add(("GPS position", ap.GpsText));

        var choice = await Shell.Current.DisplayActionSheetAsync("Copy", "Cancel", null,
            choices.Select(c => c.Title).ToArray());
        var text = choices.FirstOrDefault(c => c.Title == choice).Text;
        if (text is null) return;
        await Clipboard.Default.SetTextAsync(text);
        HistoryStatus = $"Copied {choice!.ToLowerInvariant()}";
    }

    /// <summary>The original's Locate in WifiDB: what WifiDB knows about this AP, with a link to its page there.</summary>
    [RelayCommand]
    private async Task LocateInWifiDbAsync()
    {
        if (Ap is not { } ap) return;
        HistoryStatus = "Searching WifiDB…";
        IReadOnlyList<Services.WifiDbAp> found;
        try
        {
            found = await Services.WifiDbLookup.FindAsync(ap.Bssid);
        }
        catch (Exception ex)
        {
            HistoryStatus = string.Empty;
            await Shell.Current.DisplayAlertAsync("WifiDB", $"Couldn't search WifiDB: {ex.Message}", "OK");
            return;
        }
        HistoryStatus = string.Empty;

        // The search matches partial MACs too, so prefer the exact BSSID
        var match = found.FirstOrDefault(a => string.Equals(a.Mac, ap.Bssid, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            await Shell.Current.DisplayAlertAsync("WifiDB", $"WifiDB doesn't have {ap.Bssid} yet.", "OK");
            return;
        }

        var lines = new List<string>
        {
            $"SSID: {match.Ssid}",
            $"Security: {match.Security}",
            $"Channel: {match.Channel}",
            $"First seen: {match.FirstSeen}",
            $"Last seen: {match.LastSeen}",
        };
        if (match.HighSignal is not null) lines.Add($"Best signal: {match.HighSignal}%");
        if (match.Latitude is not null && match.Longitude is not null &&
            double.TryParse(match.Latitude, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(match.Longitude, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon) &&
            (lat != 0 || lon != 0))
            lines.Add($"Position: {GpsFormatter.ToText(lat, lon)}");

        if (await Shell.Current.DisplayAlertAsync($"WifiDB: {match.Mac}", string.Join(Environment.NewLine, lines),
                "Open in WifiDB", "Close"))
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
            $"Manufacturer: {ap.Manufacturer}",
            $"Signal: {ap.Signal}%" + (ap.Rssi.HasValue ? $" ({ap.Rssi} dBm)" : ""),
            $"Channel: {ap.Channel} ({ap.FrequencyMhz} MHz)",
            $"Radio: {ap.RadioType}",
            $"Security: {ap.AuthText} / {ap.EncryptionText}",
            $"First active: {ap.FirstSeenText}",
            $"Last active: {ap.LastSeenText}",
        };
        if (ap.HasGps) lines.Add($"GPS: {ap.GpsText}");
        return string.Join(Environment.NewLine, lines);
    }
}
