using System.Text.Json;

namespace VistumblerMAUI.Services;

/// <summary>Credentials returned by redeeming a WifiDB one-time registration link.</summary>
public record WifiDbCredentials(string Username, string ApiKey, string BaseUrl);

/// <summary>
/// Redeems a WifiDB one-time registration link (…/wifidb/cp/redeem_link.php?token=…),
/// as produced by WifiDB's control panel and encoded in the registration QR code.
/// Mirrors vistumbler-android's ActivateActivity WifiDB flow: GET the link, parse the
/// {"apikey","username"} JSON, and derive the site base URL from the link's host.
/// </summary>
public static class WifiDbRegistration
{
    /// <summary>True if the scanned/pasted text looks like a WifiDB redeem link.</summary>
    public static bool IsRedeemLink(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Contains("redeem_link.php?token=", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// GETs the redeem link and returns the WifiDB username, API key, and derived site
    /// base URL (scheme://host[:port]/). Throws if the response has no apikey.
    /// </summary>
    public static async Task<WifiDbCredentials> RedeemAsync(
        string redeemUrl, HttpClient http, CancellationToken ct = default)
    {
        var json = await http.GetStringAsync(redeemUrl.Trim(), ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string apikey   = root.TryGetProperty("apikey",   out var k) ? k.GetString() ?? "" : "";
        string username = root.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(apikey))
            throw new InvalidOperationException("The WifiDB server response did not include an API key.");

        // Derive the site base URL from the redeem link's host (matches the android flow).
        var parsed  = new Uri(redeemUrl.Trim());
        string baseUrl = parsed.GetLeftPart(UriPartial.Authority) + "/";

        return new WifiDbCredentials(username, apikey, baseUrl);
    }
}
