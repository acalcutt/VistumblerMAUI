namespace VistumblerMAUI.Services;

/// <summary>How the map picks its zoom when GPS Follow mode engages.</summary>
public enum FollowZoom
{
    Auto,        // fit the fix's accuracy circle (renderer GpsFollowZoomMode.Accuracy)
    Manual,      // always use ManualZoom (renderer GpsFollowZoomMode.Fixed)
    KeepCurrent  // leave the zoom alone (renderer GpsFollowZoomMode.KeepCurrent)
}

/// <summary>
/// Persisted follow-zoom configuration (MAUI <see cref="Preferences"/>) for the map's
/// GPS Follow mode, applied to the MapLibre control when the Map page appears.
/// </summary>
public static class MapFollowSettings
{
    private const string ModeKey = "Map_FollowZoomMode";
    private const string ZoomKey = "Map_FollowZoomLevel";

    public static FollowZoom Mode
    {
        get => Preferences.Get(ModeKey, nameof(FollowZoom.Auto)) switch
        {
            nameof(FollowZoom.Manual)      => FollowZoom.Manual,
            nameof(FollowZoom.KeepCurrent) => FollowZoom.KeepCurrent,
            _                              => FollowZoom.Auto,
        };
        set => Preferences.Set(ModeKey, value.ToString());
    }

    /// <summary>Zoom level used when <see cref="Mode"/> is Manual. Clamped 1–22.</summary>
    public static double ManualZoom
    {
        get => Math.Clamp(Preferences.Get(ZoomKey, 16.0), 1, 22);
        set => Preferences.Set(ZoomKey, Math.Clamp(value, 1, 22));
    }
}
