using System.Text.Json;

namespace VistumblerMAUI.Services;

/// <summary>An AP as WifiDB has it.</summary>
public sealed record WifiDbAp(string Id, string Ssid, string Mac, string Security, string Channel,
    string FirstSeen, string LastSeen, string? Latitude, string? Longitude, string? HighSignal)
{
    /// <summary>The AP's page on the WifiDB site.</summary>
    public string PageUrl => WifiDbSettings.Url.TrimEnd('/') + "/opt/fetch.php?id=" + Uri.EscapeDataString(Id);
}

/// <summary>
/// The original Vistumbler's "Locate in WifiDB": finds an AP in WifiDB by its BSSID, through the site's search
/// API ({site}/api/search.php). The original posted to import.php, which no longer answers lookups.
/// </summary>
public static class WifiDbLookup
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>WifiDB's records for <paramref name="bssid"/>; empty when it doesn't have the AP. Throws when WifiDB can't be reached.</summary>
    public static async Task<IReadOnlyList<WifiDbAp>> FindAsync(string bssid, CancellationToken ct = default)
    {
        var url = $"{WifiDbSettings.Url.TrimEnd('/')}/api/search.php?mac={Uri.EscapeDataString(bssid)}&inc=10";
        var body = await Http.GetStringAsync(url, ct);
        using var json = JsonDocument.Parse(body);
        if (json.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<WifiDbAp>();

        return json.RootElement.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Object)
            .Select(e => new WifiDbAp(
                Get(e, "id") ?? "",
                Get(e, "ssid") ?? "",
                Get(e, "mac") ?? bssid,
                $"{Get(e, "auth")} / {Get(e, "encry")}",
                Get(e, "chan") ?? "",
                Get(e, "FA") ?? "",
                Get(e, "LA") ?? "",
                Get(e, "lat"),
                Get(e, "long"),
                Get(e, "high_sig")))
            .Where(a => a.Id.Length > 0)
            .ToList();
    }

    private static string? Get(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null && v.ToString() is { Length: > 0 } s ? s : null;
}
