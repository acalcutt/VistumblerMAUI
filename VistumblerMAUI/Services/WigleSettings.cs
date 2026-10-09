namespace VistumblerMAUI.Services;

/// <summary>
/// Optional uploads to WiGLE (wigle.net), alongside WifiDB, which stays the main destination. Off by default.
/// Uses the API name and token from the user's WiGLE account page, as WiGLE's own client does, never their password.
/// Backed by MAUI <see cref="Preferences"/>.
/// </summary>
public static class WigleSettings
{
    private const string EnabledKey    = "Wigle_Enabled";
    private const string ApiNameKey    = "Wigle_ApiName";
    private const string ApiTokenKey   = "Wigle_ApiToken";
    private const string DonateKey     = "Wigle_Donate";
    private const string EachSaveKey   = "Wigle_UploadEachSave";

    /// <summary>Where the API name and token are found.</summary>
    public const string AccountUrl = "https://wigle.net/account";
    public const string TermsUrl = "https://wigle.net/terms";

    /// <summary>Whether WiGLE uploads are offered at all. Everything else here only applies when this is on.</summary>
    public static bool Enabled
    {
        get => Preferences.Get(EnabledKey, false);
        set => Preferences.Set(EnabledKey, value);
    }

    public static string ApiName
    {
        get => Preferences.Get(ApiNameKey, string.Empty);
        set => Preferences.Set(ApiNameKey, value?.Trim() ?? string.Empty);
    }

    public static string ApiToken
    {
        get => Preferences.Get(ApiTokenKey, string.Empty);
        set => Preferences.Set(ApiTokenKey, value?.Trim() ?? string.Empty);
    }

    /// <summary>WiGLE's "donate": also let WiGLE license the data for commercial use. Off unless the user chooses it.</summary>
    public static bool Donate
    {
        get => Preferences.Get(DonateKey, false);
        set => Preferences.Set(DonateKey, value);
    }

    /// <summary>Also upload each Save &amp; Clear to WiGLE.</summary>
    public static bool UploadEachSave
    {
        get => Preferences.Get(EachSaveKey, false);
        set => Preferences.Set(EachSaveKey, value);
    }

    public static bool HasAccount => !string.IsNullOrWhiteSpace(ApiName) && !string.IsNullOrWhiteSpace(ApiToken);

    /// <summary>Whether uploads to WiGLE can happen: turned on, with an account.</summary>
    public static bool Ready => Enabled && HasAccount;
}
