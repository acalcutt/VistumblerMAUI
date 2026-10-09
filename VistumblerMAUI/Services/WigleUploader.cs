using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <param name="Success">Whether WiGLE accepted the file.</param>
/// <param name="Message">What happened, for the status line.</param>
/// <param name="ConnectionFailed">WiGLE couldn't be reached or didn't answer, rather than refusing the file.</param>
public record WigleUploadResult(bool Success, string Message, bool ConnectionFailed = false);

/// <summary>
/// Uploads to WiGLE's file API (api/v2/file/upload) the way WiGLE WiFi Wardriving does (ObservationUploader): a
/// gzipped WiGLE CSV of Wi-Fi, cell towers and Bluetooth, with Basic auth from the account's API name and token,
/// and donate=on when the user chose it.
/// </summary>
public static class WigleUploader
{
    public const string UploadUrl = "https://api.wigle.net/api/v2/file/upload";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Writes the session as a gzipped WiGLE CSV ("….csv.gz", as WiGLE's client names its uploads) with every reading
    /// of the APs, cell towers and Bluetooth devices. Returns how many networks it holds; 0 writes nothing worth sending.
    /// </summary>
    public static async Task<int> WriteSessionAsync(IDatabaseService db, IExportService export, string gzPath)
    {
        await db.InitializeAsync();
        var aps = await db.GetAllAccessPointsAsync();
        foreach (var ap in aps) ap.SignalHistory = await db.GetSignalHistoryAsync(ap.ApId);
        var radios = await SessionFileExporter.LoadRadiosAsync(db);
        if (aps.Count == 0 && radios.Count == 0) return 0;

        var csv = gzPath + ".tmp";
        try
        {
            await export.ExportToWigleCsvAsync(csv, aps, radios);
            Directory.CreateDirectory(Path.GetDirectoryName(gzPath)!);
            await using (var source = File.OpenRead(csv))
            await using (var target = new GZipStream(File.Create(gzPath), CompressionLevel.Optimal))
                await source.CopyToAsync(target);
        }
        finally
        {
            try { File.Delete(csv); } catch { /* temporary */ }
        }
        return aps.Count + radios.Count;
    }

    public static async Task<WigleUploadResult> UploadAsync(string filePath, CancellationToken ct = default)
    {
        if (!WigleSettings.HasAccount)
            return new(false, "Set your WiGLE API name and token in Settings → WiGLE");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl);
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{WigleSettings.ApiName}:{WigleSettings.ApiToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
            request.Headers.UserAgent.ParseAdd($"VistumblerMAUI/{AppInfo.Current.VersionString}");

            using var content = new MultipartFormDataContent();
            if (WigleSettings.Donate) content.Add(new StringContent("on"), "donate");
            await using var stream = File.OpenRead(filePath);
            var file = new StreamContent(stream);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(file, "file", Path.GetFileName(filePath));
            request.Content = content;

            using var response = await Http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(false, "WiGLE refused the API name or token (Settings → WiGLE)");

            // {"success":true,"results":{"transids":[{"transId":"…"}]}} or {"success":false,"message":"…"}
            string? message = null;
            bool success = false;
            try
            {
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
                    if (root.TryGetProperty("message", out var m)) message = m.ToString();
                }
            }
            catch (JsonException) { /* not JSON: report the status */ }

            if (response.IsSuccessStatusCode && success)
                return new(true, "Uploaded to WiGLE");
            return new(false, $"WiGLE: {message ?? $"HTTP {(int)response.StatusCode}"}",
                ConnectionFailed: (int)response.StatusCode >= 500);
        }
        catch (OperationCanceledException)
        {
            return new(false, "WiGLE upload timed out", ConnectionFailed: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new(false, $"WiGLE upload failed: {ex.Message}", ConnectionFailed: true);
        }
        catch (Exception ex)
        {
            return new(false, $"WiGLE upload failed: {ex.Message}");
        }
    }
}
