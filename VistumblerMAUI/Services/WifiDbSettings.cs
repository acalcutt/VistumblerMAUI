namespace VistumblerMAUI.Services;

/// <summary>
/// Persisted WifiDB account/connection settings, backed by MAUI <see cref="Preferences"/>.
/// Shared by SettingsViewModel (which edits them) and MapViewModel (which reads the base
/// URL when requesting history tiles). Field naming mirrors VistumblerCS's WifiDB settings.
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

    /// <summary>API base, e.g. "https://wifidb.net/api" — append "/tilejson.php" etc.</summary>
    public static string ApiBaseUrl => Url.TrimEnd('/') + "/api";
}
