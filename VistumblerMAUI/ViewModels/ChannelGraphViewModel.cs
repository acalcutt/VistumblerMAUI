using CommunityToolkit.Mvvm.ComponentModel;
using VistumblerMAUI.Controls;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Feeds the channel-graph drawable from the live scan list, filtered to the selected band
/// (2.4 / 5 / 6 GHz). Each active AP becomes a bell curve coloured from its BSSID.
/// </summary>
public partial class ChannelGraphViewModel : ObservableObject
{
    private readonly ScanViewModel _scan;

    public ChannelGraphDrawable Graph { get; } = new();

    /// <summary>Raised after the entries change so the page can invalidate its GraphicsView.</summary>
    public event Action? GraphUpdated;

    public IReadOnlyList<string> BandOptions { get; } = new[] { "2.4 GHz", "5 GHz", "6 GHz" };

    [ObservableProperty] private string _selectedBand = "2.4 GHz";
    [ObservableProperty] private bool   _useRssi;
    [ObservableProperty] private string _statusText = string.Empty;

    public ChannelGraphViewModel(ScanViewModel scan) => _scan = scan;

    partial void OnSelectedBandChanged(string value) { Graph.Band = ParseBand(value); Refresh(); }
    partial void OnUseRssiChanged(bool value)         { Graph.UseRssi = value; Refresh(); }

    /// <summary>Rebuild the graph entries from the current live APs.</summary>
    public void Refresh()
    {
        (int min, int max, int half) = Graph.Band switch
        {
            GraphBand.FiveGHz => (5150, 5925, 20),
            GraphBand.SixGHz  => (5925, 7130, 20),
            _                 => (2400, 2500, 11),
        };

        var entries = new List<ChannelEntry>();
        foreach (var ap in _scan.AccessPoints)
        {
            if (!ap.IsActive) continue;                 // only currently-in-range APs
            int f = ap.FrequencyMhz;
            if (f < min || f >= max) continue;

            var (fill, stroke) = ColorFor(ap.Bssid);
            entries.Add(new ChannelEntry(ap.Ssid, f, half, ap.Signal ?? 0, ap.Rssi ?? -100, fill, stroke));
        }

        Graph.SetEntries(entries);
        StatusText = $"{entries.Count} active APs on {SelectedBand}";
        GraphUpdated?.Invoke();
    }

    private static GraphBand ParseBand(string s) => s switch
    {
        "5 GHz" => GraphBand.FiveGHz,
        "6 GHz" => GraphBand.SixGHz,
        _       => GraphBand.TwoPointFourGHz,
    };

    // Stable per-AP colour from its BSSID (opaque stroke + translucent fill).
    private static (Color Fill, Color Stroke) ColorFor(string bssid)
    {
        int hash = (bssid ?? string.Empty).GetHashCode();
        double hue = Math.Abs(hash) % 360 / 360.0;
        var stroke = Color.FromHsla(hue, 0.6, 0.45);
        return (stroke.WithAlpha(0.28f), stroke);
    }
}
