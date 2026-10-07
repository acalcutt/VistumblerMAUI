using System.Net.Http.Headers;
using System.Text.Json;

namespace VistumblerMAUI.Services;

/// <param name="Success">Whether WifiDB accepted the file for import.</param>
/// <param name="Message">WifiDB's message, or the error.</param>
/// <param name="ImportId">WifiDB's number for the queued import.</param>
/// <param name="FileHash">MD5 of the file, for <see cref="WifiDbUploader.CheckStatusAsync"/>.</param>
/// <param name="ConnectionFailed">WifiDB couldn't be reached or didn't answer in time, rather than refusing the file.</param>
public record WifiDbUploadResult(bool Success, string Message, string? ImportId = null, string? FileHash = null,
    bool ConnectionFailed = false)
{
    /// <summary>WifiDB already has this file, imported or waiting: an earlier upload got through.</summary>
    public bool AlreadyKnown => !Success && (Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                                             Message.Contains("already waiting", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Uploads Vistumbler files to WifiDB's import API (api/v2/import.php under the site URL in Settings → WifiDB),
/// as the original Vistumbler's _UploadFileToWifiDB and VistumblerCS's Upload to WifiDB do. WifiDB queues the
/// file and imports it later; <see cref="CheckStatusAsync"/> follows it by its hash.
/// </summary>
public static class WifiDbUploader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>The import endpoint for the configured WifiDB site, e.g. https://wifidb.net/api/v2/import.php.</summary>
    public static string ImportUrl => WifiDbSettings.Url.TrimEnd('/') + "/api/v2/import.php";

    public static async Task<WifiDbUploadResult> UploadAsync(string filePath, string user, string apiKey,
        string otherUsers, string title, string notes, CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            void Add(string name, string value)
            {
                if (!string.IsNullOrWhiteSpace(value)) content.Add(new StringContent(value.Trim()), name);
            }
            Add("username", user);
            Add("apikey", apiKey);
            Add("otherusers", otherUsers);
            Add("title", title);
            Add("notes", notes);

            await using var stream = File.OpenRead(filePath);
            var file = new StreamContent(stream);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(file, "file", Path.GetFileName(filePath));

            using var response = await Http.PostAsync(ImportUrl, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new(false, $"WifiDB returned HTTP {(int)response.StatusCode}: {Trim(body)}",
                    ConnectionFailed: (int)response.StatusCode >= 500);   // the server is down, not refusing the file

            // {"import":{"title","user","message","importnum","filehash"}} or {"error":"..."}
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var error))
                    return new(false, error.ToString());
                if (root.TryGetProperty("import", out var import) && import.ValueKind == JsonValueKind.Object)
                {
                    if (import.TryGetProperty("error", out var importError))
                        return new(false, importError.ToString());
                    return new(true,
                        Get(import, "message") ?? "File uploaded to WifiDB.",
                        Get(import, "importnum"),
                        Get(import, "filehash"));
                }
            }
            return new(false, $"Unexpected response from WifiDB: {Trim(body)}");
        }
        catch (JsonException)
        {
            return new(false, "WifiDB's response could not be read.");
        }
        catch (OperationCanceledException)
        {
            // HttpClient's own timeout, or the caller's (WifiDbUploadQueue gives each file a time limit)
            return new(false, "Upload timed out", ConnectionFailed: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new(false, $"Upload failed: {ex.Message}", ConnectionFailed: true);
        }
        catch (Exception ex)
        {
            return new(false, $"Upload failed: {ex.Message}");
        }
    }

    /// <summary>Where an uploaded file is in WifiDB's import queue: waiting, importing, imported or unknown.</summary>
    public static async Task<string> CheckStatusAsync(string fileHash, CancellationToken ct = default)
    {
        try
        {
            var url = $"{ImportUrl}?func=check_hash&hash={Uri.EscapeDataString(fileHash)}";
            var body = await Http.GetStringAsync(url, ct);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error)) return error.ToString();
            if (!root.TryGetProperty("scheduling", out var scheduling) || scheduling.ValueKind != JsonValueKind.Object)
                return $"Unexpected response from WifiDB: {Trim(body)}";

            if (scheduling.TryGetProperty("waiting", out _))
                return "Waiting in WifiDB's import queue.";
            if (scheduling.TryGetProperty("importing", out var importing))
            {
                var ap = Get(importing, "ap");
                var total = Get(importing, "tot");
                return ap is not null && total is not null
                    ? $"Importing now ({ap} of {total} APs)."
                    : "Importing now.";
            }
            if (scheduling.TryGetProperty("finished", out var finished))
            {
                var aps = Get(finished, "aps");
                var gps = Get(finished, "gps");
                return aps is not null
                    ? $"Imported: {aps} APs, {gps ?? "0"} GPS points."
                    : "Imported.";
            }
            return "WifiDB doesn't know this file yet.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return $"Status check failed: {ex.Message}";
        }
    }

    private static string? Get(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;

    private static string Trim(string text) => text.Length > 200 ? text[..200] + "…" : text;
}
