using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Represents one toggleable wifidb.net history overlay on the map — one or more
/// per-bucket vector tile layers, each read from that bucket's published PMTiles
/// archive (see <see cref="Services.WifiDbTileSources"/>).
/// </summary>
public partial class HistoryLayerState : ObservableObject
{
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isLoading;

    /// <summary>Stable ID used to name the MapLibre layer (e.g. "hist_daily_circles").</summary>
    public string  Id                { get; init; } = "";

    /// <summary>Button label shown in the UI.</summary>
    public string  Label             { get; init; } = "";

    /// <summary>Hex color for the active button state and circle paint.</summary>
    public string  ActiveColor       { get; init; } = "#3BB2D0";

    /// <summary>
    /// WifiDB history-bucket name(s) backing this layer (e.g. "daily", "weekly",
    /// "cell_daily"). Each bucket gets its own dynamically-added vector source
    /// ("WifiDB_{bucket}", loaded from the TileJSON that
    /// <see cref="Services.WifiDbTileSources.TileJsonUrlFor"/> resolves) and circle
    /// layer ("hist_{bucket}_circles" with source-layer "{bucket}"). Multiple buckets
    /// let one button (e.g. "Cell Networks") toggle several tiers at once, matching
    /// VistumblerCS's combined toggle.
    /// </summary>
    public string[]? Buckets         { get; init; }

    // Set by MapViewModel after construction so the command can call back into the VM.
    public IRelayCommand ToggleCommand { get; set; } = new RelayCommand(() => { });
}
