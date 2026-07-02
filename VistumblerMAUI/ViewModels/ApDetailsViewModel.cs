using CommunityToolkit.Mvvm.ComponentModel;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using VistumblerMAUI.Controls;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Backs the AP details page: loads a single access point (by BSSID) plus its recorded
/// signal history, and feeds the signal-over-time graph. Reached from the Scan list.
/// </summary>
public partial class ApDetailsViewModel : ObservableObject, IQueryAttributable
{
    private readonly IDatabaseService _db;

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
        await LoadAsync(Uri.UnescapeDataString(bssid));
    }

    private async Task LoadAsync(string bssid)
    {
        await _db.InitializeAsync();

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
