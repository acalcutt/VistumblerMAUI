using System.Text.Json;
using Vistumbler.Core.Models;
using VistumblerMAUI.Localization;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// "Saved this session": the APs Save &amp; Clear took out of the list, at the point each was strongest, so the
/// session's earlier drive stays on the map after the list is cleared. A layer button beside the WifiDB history
/// layers; drawn just under the live dots in the dimmer "no longer in range" colours, and redrawn after each save.
/// The points come from the session database (ScanViewModel.Saves.cs), so a resumed session has them too.
/// </summary>
public partial class MapViewModel
{
    private const string SavedLayerId = "saved";
    private const string SavedSource = "saved-source";
    private const string SavedCircles = "saved-circles";
    private bool _savedDrawn;
    private bool _savedHooked;

    private HistoryLayerState? SavedLayer => HistoryLayers.FirstOrDefault(l => l.Id == SavedLayerId);

    /// <summary>What WifiDB history layers go under: the saved points when drawn, so this session's stay on top
    /// of the history, otherwise the live dots ("ap-circles", always above everything).</summary>
    private string TopHistoryAnchor => _savedDrawn ? SavedCircles : "ap-circles";

    private void HookSavedPoints()
    {
        if (_savedHooked) return;
        _savedHooked = true;
        _scan.SavesChanged += async (_, _) =>
        {
            if (SavedLayer?.IsActive == true) await DrawSavedPointsAsync();
        };
    }

    private async Task ToggleSavedLayerAsync(HistoryLayerState layer)
    {
        if (layer.IsActive) await DrawSavedPointsAsync();
        else
        {
            RemoveSavedLayer();
            StatusMessage = Loc.T("Map_LayerOff", layer.Label);
        }
    }

    /// <summary>Draws the saved points, if the layer is on. Also called when a style has loaded.</summary>
    private async Task DrawSavedPointsAsync()
    {
        if (_controller is null) return;
        List<SavedPoint> points;
        try { points = await _db.GetSavedPointsAsync(); }
        catch (Exception ex)
        {
            Services.DebugLog.Write($"[SavedPoints] couldn't read: {ex.Message}");
            return;
        }
        var geoJson = SavedGeoJson(points);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_controller is null) return;
            try
            {
                RemoveSavedLayer();
                var (open, wep, secure) = Services.MapColors.Hashed("live_dead");
                double s = Services.MapPointSize.Scale;
                _controller.AddGeoJsonSource(SavedSource, geoJson);
                _controller.AddCircleLayer(SavedCircles, SavedSource, belowLayerId: "ap-circles", sourceLayer: null,
                    properties: new Dictionary<string, object?>
                    {
                        ["circle-radius"] = new object[] {
                            "interpolate", new object[] { "exponential", 1.5 }, new object[] { "zoom" },
                            1.0, 3.0 * s, 12.0, 3.0 * s, 20.0, 18.0 * s,
                        },
                        ["circle-color"] = new Dictionary<string, object?>
                        {
                            ["property"] = "sectype",
                            ["stops"] = new object[] { new object[] { 1, open }, new object[] { 2, wep }, new object[] { 3, secure } },
                        },
                        ["circle-stroke-width"] = 0.4,
                        ["circle-stroke-color"] = "#FFFFFF",
                        ["circle-opacity"] = 0.6,
                    });
                _savedDrawn = true;
                StatusMessage = Loc.T("Map_SavedPoints", points.Count);
            }
            catch (Exception ex)
            {
                Services.DebugLog.Write($"[SavedPoints] couldn't draw: {ex.Message}");   // style not ready; redrawn when it is
            }
        });
    }

    private void RemoveSavedLayer()
    {
        if (_controller is null || !_savedDrawn) return;
        try { _controller.RemoveLayer(SavedCircles); } catch { /* already gone */ }
        try { _controller.RemoveSource(SavedSource); } catch { /* already gone */ }
        _savedDrawn = false;
    }

    // Written like BuildGeoJson (Utf8JsonWriter): SSIDs can hold control characters, and a comma-decimal
    // language would otherwise write "11,516670"
    private static string SavedGeoJson(List<SavedPoint> points)
    {
        if (points.Count == 0) return EmptyGeoJson;
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("type", "FeatureCollection");
            w.WriteStartArray("features");
            foreach (var p in points)
            {
                if (!double.IsFinite(p.Latitude) || !double.IsFinite(p.Longitude)) continue;
                w.WriteStartObject();
                w.WriteString("type", "Feature");
                w.WriteStartObject("geometry");
                w.WriteString("type", "Point");
                w.WriteStartArray("coordinates");
                w.WriteNumberValue(Math.Round(p.Longitude, 6));
                w.WriteNumberValue(Math.Round(p.Latitude, 6));
                w.WriteEndArray();
                w.WriteEndObject();
                w.WriteStartObject("properties");
                w.WriteString("ssid", p.Ssid);
                w.WriteString("bssid", p.Bssid);
                w.WriteString("auth", p.Authentication);
                w.WriteString("encry", p.Encryption);
                w.WriteString("radio", p.RadioType);
                w.WriteString("manuf", p.Manufacturer);
                w.WriteNumber("rssi", p.Rssi ?? 0);
                w.WriteNumber("signal", p.Signal ?? 0);
                w.WriteNumber("channel", p.Channel);
                w.WriteNumber("sectype", p.SecurityType);
                w.WriteString("la", p.LastSeen == default ? "" : p.LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
