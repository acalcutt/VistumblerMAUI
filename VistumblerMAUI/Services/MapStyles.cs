namespace VistumblerMAUI.Services;

/// <summary>
/// Map basemap style presets and the persisted user selection (MAUI <see cref="Preferences"/>).
/// The selection survives restarts and is read by MapViewModel when the map loads.
/// </summary>
public static class MapStyles
{
    private const string StyleUrlKey = "Map_StyleUrl";

    public const string DefaultUrl = "https://tiles.wifidb.net/styles/WDB_OSM/style.json";

    /// <summary>Label shown in the picker for a user-entered URL.</summary>
    public const string CustomName = "Custom…";

    /// <summary>Built-in style presets (display name → style.json URL), in menu order.</summary>
    public static readonly IReadOnlyList<(string Name, string Url)> Presets = new[]
    {
        ("WifiDB OSM",                 "https://tiles.wifidb.net/styles/WDB_OSM/style.json"),
        ("WifiDB Color Relief",        "https://tiles.wifidb.net/styles/WDB_COLOR_RELIEF/style.json"),
        ("WifiDB Color Relief (Dark)", "https://tiles.wifidb.net/styles/WDB_COLOR_RELIEF_DARK/style.json"),
    };

    /// <summary>The currently selected basemap style URL. Never stored blank.</summary>
    public static string StyleUrl
    {
        get => Preferences.Get(StyleUrlKey, DefaultUrl);
        set => Preferences.Set(StyleUrlKey, string.IsNullOrWhiteSpace(value) ? DefaultUrl : value.Trim());
    }
}
