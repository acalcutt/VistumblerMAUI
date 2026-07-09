using CommunityToolkit.Mvvm.ComponentModel;
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

    public ApDetailsViewModel(IDatabaseService db) => _db = db;

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
                await Task.Delay(LiveRefreshIntervalMs, ct);
                if (!ct.IsCancellationRequested)
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
}
