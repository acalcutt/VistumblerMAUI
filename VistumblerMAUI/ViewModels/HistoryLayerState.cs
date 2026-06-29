using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Represents one toggleable wifidb.net history overlay on the map.
/// Either a GeoJSON-fetched layer (daily data) or a vector tile source-layer
/// that is already embedded in the WifiDB style.json (weekly, monthly, …).
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

    // ── GeoJSON path ───────────────────────────────────────────────────────
    /// <summary>If non-null this layer fetches GeoJSON from this URL on activate.</summary>
    public string? GeoJsonUrl        { get; init; }

    // ── Vector tile path ──────────────────────────────────────────────────
    /// <summary>
    /// Vector tile source already present in the WifiDB style
    /// (e.g. "WifiDB_newest", "WifiDB", "WifiDB_cells").
    /// Null for GeoJSON layers.
    /// </summary>
    public string? VectorSourceId    { get; init; }

    /// <summary>
    /// Source-layer name inside the vector tile source
    /// (e.g. "WifiDB_weekly", "cell_networks").
    /// </summary>
    public string? VectorSourceLayer { get; init; }

    public bool IsGeoJsonLayer => GeoJsonUrl is not null;

    // Set by MapViewModel after construction so the command can call back into the VM.
    public IRelayCommand ToggleCommand { get; set; } = new RelayCommand(() => { });
}
