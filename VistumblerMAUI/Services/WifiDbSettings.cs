namespace VistumblerMAUI.Services;

/// <summary>
/// Persisted WifiDB account/connection settings, backed by MAUI <see cref="Preferences"/>.
/// Edited by SettingsViewModel and read when talking to the WifiDB site. Field naming
/// mirrors VistumblerCS's WifiDB settings.
///
/// This is the account, not the map data: history overlays come from the tile archives
/// on data.wifidb.net and are resolved by <see cref="WifiDbTileSources"/>.
/// </summary>
public static class WifiDbSettings
{
    private const string UrlKey    = "WifiDb_Url";
    private const string UserKey   = "WifiDb_User";
    private const string ApiKeyKey = "WifiDb_ApiKey";

    /// <summary>Default WifiDB site root (matches VistumblerCS's WifiDbUrl default).</summary>
    public const string DefaultUrl = "https://wifidb.net/";

    /// <summary>WifiDB site root, e.g. "https://wifidb.net/". Never stored blank.</summary>
    public static string Url
    {
        get => Preferences.Get(UrlKey, DefaultUrl);
        set => Preferences.Set(UrlKey, string.IsNullOrWhiteSpace(value) ? DefaultUrl : value.Trim());
    }

    /// <summary>WifiDB username.</summary>
    public static string User
    {
        get => Preferences.Get(UserKey, string.Empty);
        set => Preferences.Set(UserKey, value?.Trim() ?? string.Empty);
    }

    /// <summary>WifiDB API key.</summary>
    public static string ApiKey
    {
        get => Preferences.Get(ApiKeyKey, string.Empty);
        set => Preferences.Set(ApiKeyKey, value?.Trim() ?? string.Empty);
    }
}
