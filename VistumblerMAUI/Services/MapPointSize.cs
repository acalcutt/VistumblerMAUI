namespace VistumblerMAUI.Services;

/// <summary>
/// How large the map draws AP points (live scan and WifiDB history), as a multiple of the built-in sizes.
/// The renderer already scales by display density, so this is for taste and eyesight rather than screen
/// resolution. Settings → Map; the map picks it up the next time it appears.
/// </summary>
public static class MapPointSize
{
    private const string ScaleKey = "Map_PointScale";

    public const double Min = 0.5;
    public const double Max = 3.0;

    /// <summary>Multiplier on every AP circle radius, 0.5–3.0 in steps of 0.25. Default 1.</summary>
    public static double Scale
    {
        get => Preferences.Get(ScaleKey, 1.0);
        set => Preferences.Set(ScaleKey, Math.Round(Math.Clamp(value, Min, Max) * 4) / 4);
    }
}
