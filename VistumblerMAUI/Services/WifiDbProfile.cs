using System.Text.Json;

namespace VistumblerMAUI.Services;

/// <summary>An import of the user's that WifiDB has finished.</summary>
public record WifiDbImport(int Id, string Title, string File, DateTime? Date, int Aps, int Gps, int? Efficiency, string Url);

/// <summary>A file of the user's still in WifiDB's queue, or one it couldn't import.</summary>
public record WifiDbQueuedFile(string File, string? Progress, string? Error);

/// <summary>What api/v2/profile.php says about the signed-in user, keyed by file hash (lower case) where it's about files.</summary>
public sealed class WifiDbProfileInfo
{
    public string    Username    { get; init; } = string.Empty;
    public string    Url         { get; init; } = string.Empty;
    public DateTime? JoinDate    { get; init; }
    public int       Files       { get; init; }
    public long      Aps         { get; init; }
    public long      Gps         { get; init; }
    public long      NewAps      { get; init; }
    public long      NewCells    { get; init; }
    public long      NewBluetooth { get; init; }
    public int?      Efficiency  { get; init; }
    public DateTime? FirstImport { get; init; }
    public DateTime? LastImport  { get; init; }
    public Dictionary<string, WifiDbQueuedFile> Waiting   { get; } = new();
    public Dictionary<string, WifiDbQueuedFile> Importing { get; } = new();
    public Dictionary<string, WifiDbQueuedFile> Bad       { get; } = new();
    public Dictionary<string, WifiDbImport>     Imports   { get; } = new();
}

/// <summary>
/// The signed-in user's WifiDB profile (api/v2/profile.php): all-time totals, and their recent imports and
/// queued or failed files by hash, which is how the Uploads page finds what became of each save.
/// </summary>
public static class WifiDbProfile
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string ProfileUrl => WifiDbSettings.Url.TrimEnd('/') + "/api/v2/profile.php";

    /// <summary>Fetches the profile with the newest <paramref name="imports"/> imports (WifiDB allows up to 100).</summary>
    /// <exception cref="Exception">WifiDB couldn't be reached, or refused the account (its message).</exception>
    public static async Task<WifiDbProfileInfo> FetchAsync(string user, string apiKey, int imports = 100, CancellationToken ct = default)
    {
        // POST, so the key isn't in the URL (or WifiDB's access log)
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = user, ["apikey"] = apiKey, ["inc"] = imports.ToString(),
        });
        using var response = await Http.PostAsync(ProfileUrl, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"WifiDB returned HTTP {(int)response.StatusCode}");

        JsonDocument json;
        try { json = JsonDocument.Parse(body); }
        catch (JsonException) { throw new InvalidOperationException("WifiDB's answer couldn't be read; it may not have the profile API yet."); }
        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
                throw new InvalidOperationException(error.ToString());
            if (!root.TryGetProperty("profile", out var p))
                throw new InvalidOperationException("WifiDB's answer had no profile.");

            var totals = p.TryGetProperty("totals", out var t) ? t : default;
            var info = new WifiDbProfileInfo
            {
                Username = Str(p, "username") ?? user,
                Url = Str(p, "url") ?? "",
                JoinDate = Date(p, "join_date"),
                Files = (int)Num(totals, "files"),
                Aps = Num(totals, "aps"),
                Gps = Num(totals, "gps"),
                NewAps = Num(totals, "new_aps"),
                NewCells = Num(totals, "new_cells"),
                NewBluetooth = Num(totals, "new_bt"),
                Efficiency = totals.ValueKind == JsonValueKind.Object && totals.TryGetProperty("efficiency", out var e) && e.ValueKind == JsonValueKind.Number ? (int)e.GetDouble() : null,
                FirstImport = Date(totals, "first_import"),
                LastImport = Date(totals, "last_import"),
            };

            if (p.TryGetProperty("queue", out var queue))
            {
                ReadQueue(queue, "waiting", info.Waiting);
                ReadQueue(queue, "importing", info.Importing);
                ReadQueue(queue, "bad", info.Bad);
            }
            if (p.TryGetProperty("imports", out var imp) && imp.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in rows.EnumerateArray())
                {
                    var hash = Str(r, "hash")?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(hash)) continue;
                    info.Imports[hash] = new WifiDbImport(
                        (int)Num(r, "id"), Str(r, "title") ?? "", Str(r, "file") ?? "", Date(r, "date"),
                        (int)Num(r, "aps"), (int)Num(r, "gps"),
                        r.TryGetProperty("efficiency", out var eff) && eff.ValueKind == JsonValueKind.Number ? eff.GetInt32() : null,
                        Str(r, "url") ?? "");
                }
            }
            return info;
        }
    }

    private static void ReadQueue(JsonElement queue, string name, Dictionary<string, WifiDbQueuedFile> into)
    {
        if (!queue.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array) return;
        foreach (var f in list.EnumerateArray())
        {
            var hash = Str(f, "hash")?.ToLowerInvariant();
            if (string.IsNullOrEmpty(hash)) continue;
            var ap = Str(f, "ap");
            var tot = Str(f, "tot");
            into.TryAdd(hash, new WifiDbQueuedFile(Str(f, "file_orig") ?? "",
                ap is not null && tot is not null ? $"{ap} / {tot}" : null, Str(f, "error_msg")));
        }
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.ToString() : null;

    private static long Num(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        return long.TryParse(v.ToString(), out var s) ? s : 0;
    }

    // WifiDB's dates are "yyyy-MM-dd HH:mm:ss" in UTC
    private static DateTime? Date(JsonElement obj, string name) =>
        Str(obj, name) is { } s && DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
            ? d : null;
}
