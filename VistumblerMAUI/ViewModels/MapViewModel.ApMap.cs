using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// One AP's maps, the original Vistumbler's KML "Selected AP" export drawn on the built-in map instead of in
/// Google Earth: its signal map (every place it was heard, coloured in the original's six signal bands), its
/// signal circle (radius 100 + RSSI metres at the strongest point) and its range circle (out to the farthest
/// point it was heard, at least 10 m). Opened from the AP details page.
/// </summary>
public partial class MapViewModel
{
    private const string ApMapPoints = "apmap-points", ApMapSignal = "apmap-signal", ApMapRange = "apmap-range";

    // Kept so the overlays can be drawn again after a style reload, which drops every layer
    private (string Points, string Signal, string Range)? _apMapGeoJson;
    private (double Lat, double Lon, double Zoom)? _apMapCamera;
    private bool _apMapDrawn;

    [ObservableProperty] private string _apMapTitle = string.Empty;
    [ObservableProperty] private bool _hasApMap;

    // The original's SigCat1 … SigCat6: 1-16, 17-32, 33-48, 49-64, 65-80, 81-100 %
    private static readonly string[] SignalBandColors = { "#d7191c", "#fd8d3c", "#fecc5c", "#a6d96a", "#1a9641", "#006837" };

    private string? _apMapBssid;
    private int _apMapCount;
    private bool _apMapRefreshing;

    /// <summary>Loads an AP's signal history and draws its maps, centring the map on them.</summary>
    public Task ShowApMapAsync(string bssid) => LoadApMapAsync(bssid, recentre: true);

    /// <summary>
    /// Redraws the AP map if the AP has been heard somewhere new since, so it fills in live while scanning: WiGLE's
    /// site survey. Called by the map's live timer.
    /// </summary>
    public async Task RefreshApMapAsync()
    {
        if (_apMapBssid is not { } bssid || _apMapRefreshing) return;
        _apMapRefreshing = true;
        try { await LoadApMapAsync(bssid, recentre: false); }
        catch (Exception ex) { Services.DebugLog.Write($"[ApMap] refresh failed: {ex.Message}"); }
        finally { _apMapRefreshing = false; }
    }

    private async Task LoadApMapAsync(string bssid, bool recentre)
    {
        await _db.InitializeAsync();
        var ap = await _db.GetAccessPointByBssidAsync(bssid);
        if (ap is null) { if (recentre) StatusMessage = Localization.Loc.T("Map_NotInSession", bssid); return; }

        var points = (await _db.GetSignalHistoryAsync(ap.ApId))
            .Where(h => h.Signal > 0 && h.Latitude is { } lat && h.Longitude is { } lon && (lat != 0 || lon != 0))
            .Select(h => (Lat: h.Latitude!.Value, Lon: h.Longitude!.Value, h.Signal, h.Rssi))
            .ToList();
        if (points.Count == 0) { if (recentre) StatusMessage = Localization.Loc.T("Map_NoPositions", bssid); return; }
        if (!recentre && points.Count == _apMapCount) return;   // nothing new
        if (!recentre && _apMapBssid != bssid) return;          // cleared while loading
        _apMapBssid = bssid;
        _apMapCount = points.Count;

        var best = points.MaxBy(p => (p.Rssi ?? int.MinValue, p.Signal));
        double signalRadius = best.Rssi is { } rssi ? Math.Max(1, 100 + rssi) : Math.Max(1, best.Signal);
        double rangeRadius = Math.Max(10, points.Max(p => DistanceMeters(best.Lat, best.Lon, p.Lat, p.Lon)));

        _apMapGeoJson = (PointsGeoJson(points), CircleGeoJson(best.Lat, best.Lon, signalRadius), CircleGeoJson(best.Lat, best.Lon, rangeRadius));
        if (recentre) _apMapCamera = (best.Lat, best.Lon, ZoomForRadius(best.Lat, rangeRadius));
        ApMapTitle = Localization.Loc.T("Map_ApMapTitle", string.IsNullOrEmpty(ap.Ssid) ? ap.Bssid : ap.Ssid, points.Count, rangeRadius.ToString("0"));
        HasApMap = true;
        if (recentre) StatusMessage = ApMapTitle;
        if (recentre || !_apMapDrawn) DrawApMap();
        else UpdateApMapSources();
    }

    [RelayCommand]
    private void ClearApMap()
    {
        _apMapBssid = null;
        _apMapCount = 0;
        RemoveApMapLayers();
        _apMapGeoJson = null;
        _apMapCamera = null;
        HasApMap = false;
        ApMapTitle = string.Empty;
    }

    /// <summary>Draws the AP map, if there is one. Also called when a style has loaded (OnMapControllerReady).</summary>
    private void DrawApMap()
    {
        if (_controller is null || _apMapGeoJson is not { } g) return;
        try
        {
            RemoveApMapLayers();
            _controller.AddGeoJsonSource(ApMapRange, g.Range);
            _controller.AddGeoJsonSource(ApMapSignal, g.Signal);
            _controller.AddGeoJsonSource(ApMapPoints, g.Points);
            _controller.AddFillLayer("apmap-range-fill", ApMapRange, null, null, new Dictionary<string, object?>
            {
                ["fill-color"] = "#1e88e5", ["fill-opacity"] = 0.12,
            });
            _controller.AddLineLayer("apmap-range-line", ApMapRange, null, null, new Dictionary<string, object?>
            {
                ["line-color"] = "#1e88e5", ["line-width"] = 2.0,
            });
            _controller.AddFillLayer("apmap-signal-fill", ApMapSignal, null, null, new Dictionary<string, object?>
            {
                ["fill-color"] = "#fb8c00", ["fill-opacity"] = 0.25,
            });
            _controller.AddLineLayer("apmap-signal-line", ApMapSignal, null, null, new Dictionary<string, object?>
            {
                ["line-color"] = "#fb8c00", ["line-width"] = 2.0,
            });
            _controller.AddCircleLayer("apmap-points-circles", ApMapPoints, null, null, new Dictionary<string, object?>
            {
                ["circle-radius"] = new Dictionary<string, object?>
                {
                    ["base"] = 1.5,
                    ["stops"] = new object[] { new object[] { 10, 3.0 * MapPointSize.Scale }, new object[] { 20, 14.0 * MapPointSize.Scale } },
                },
                ["circle-color"] = new Dictionary<string, object?>
                {
                    ["property"] = "band",
                    ["type"] = "categorical",
                    ["stops"] = SignalBandColors.Select((c, i) => (object)new object[] { i + 1, c }).ToArray(),
                },
                ["circle-stroke-width"] = 0.5,
                ["circle-stroke-color"] = "#FFFFFF",
            });
            _apMapDrawn = true;

            if (_apMapCamera is { } cam)
            {
                _controller.EaseTo(cam.Lat, cam.Lon, cam.Zoom, durationMs: 600);
                _apMapCamera = null;   // only once; later redraws after a style reload keep the user's view
            }
        }
        catch (Exception ex)
        {
            Services.DebugLog.Write($"[ApMap] couldn't draw: {ex.Message}");   // style not ready; redrawn when it is
        }
    }

    // New data for the drawn layers, without removing and re-adding them (which would flicker)
    private void UpdateApMapSources()
    {
        if (_controller is null || _apMapGeoJson is not { } g) return;
        try
        {
            _controller.SetGeoJsonSource(ApMapRange, g.Range);
            _controller.SetGeoJsonSource(ApMapSignal, g.Signal);
            _controller.SetGeoJsonSource(ApMapPoints, g.Points);
        }
        catch (Exception ex)
        {
            Services.DebugLog.Write($"[ApMap] couldn't update: {ex.Message}");
            DrawApMap();
        }
    }

    /// <summary>Saves the AP's signal map as GeoJSON or KML (WiGLE's site survey export) and offers it to share.</summary>
    [RelayCommand]
    private async Task ExportApMapAsync()
    {
        if (_apMapBssid is not { } bssid) return;
        var choice = await Shell.Current.DisplayActionSheetAsync(Localization.Loc.T("Map_ExportSignalMap"), Localization.Loc.T("Common_Cancel"), null, "GeoJSON", "KML");
        if (choice is not ("GeoJSON" or "KML")) return;
        try
        {
            var ap = await _db.GetAccessPointByBssidAsync(bssid);
            if (ap is null) return;
            ap.SignalHistory = await _db.GetSignalHistoryAsync(ap.ApId);
            string name = $"signalmap_{bssid.Replace(":", "")}_{DateTime.Now:yyyyMMdd_HHmmss}" + (choice == "KML" ? ".kml" : ".geojson");
            var (folder, _) = Services.ExportLocation.Resolve();
            var saved = await Services.SaveFolder.SaveAsync(folder, name, path => choice == "KML"
                ? Services.SignalMapExport.WriteKmlSignalMapAsync(path, new[] { ap }, $"{ap.Ssid} {ap.Bssid}")
                : Services.SignalMapExport.WriteGeoJsonAsync(path, new[] { ap }, signalMap: true))
                ?? throw new IOException("Nothing was written.");
            StatusMessage = Localization.Loc.T("Common_Saved", Services.SaveFolder.Describe(saved));
            var (local, _) = await Services.SaveFolder.GetLocalFileAsync(saved);
            try { await Share.Default.RequestAsync(new ShareFileRequest { Title = name, File = new ShareFile(local) }); }
            catch { /* saved either way */ }
        }
        catch (Exception ex)
        {
            StatusMessage = Localization.Loc.T("Common_ExportFailed", ex.Message);
        }
    }

    private void RemoveApMapLayers()
    {
        if (_controller is null || !_apMapDrawn) return;
        foreach (var layer in new[] { "apmap-points-circles", "apmap-signal-line", "apmap-signal-fill", "apmap-range-line", "apmap-range-fill" })
            try { _controller.RemoveLayer(layer); } catch { /* already gone */ }
        foreach (var source in new[] { ApMapPoints, ApMapSignal, ApMapRange })
            try { _controller.RemoveSource(source); } catch { /* already gone */ }
        _apMapDrawn = false;
    }

    private static int SignalBand(int signal) => signal switch
    {
        <= 16 => 1, <= 32 => 2, <= 48 => 3, <= 64 => 4, <= 80 => 5, _ => 6,
    };

    private static string PointsGeoJson(IEnumerable<(double Lat, double Lon, int Signal, int? Rssi)> points)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("{\"type\":\"FeatureCollection\",\"features\":[");
        bool first = true;
        foreach (var p in points)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"type\":\"Feature\",\"properties\":{\"band\":").Append(SignalBand(p.Signal))
              .Append(",\"signal\":").Append(p.Signal)
              .Append("},\"geometry\":{\"type\":\"Point\",\"coordinates\":[")
              .Append(p.Lon.ToString("F7", inv)).Append(',').Append(p.Lat.ToString("F7", inv)).Append("]}}");
        }
        return sb.Append("]}").ToString();
    }

    // A 64-sided polygon approximating a circle of radiusMeters around the point
    private static string CircleGeoJson(double lat, double lon, double radiusMeters)
    {
        var inv = CultureInfo.InvariantCulture;
        double dLat = radiusMeters / 111_320.0;
        double dLon = radiusMeters / (111_320.0 * Math.Max(0.01, Math.Cos(lat * Math.PI / 180)));
        var ring = new StringBuilder();
        for (int i = 0; i <= 64; i++)
        {
            double a = 2 * Math.PI * i / 64;
            if (i > 0) ring.Append(',');
            ring.Append('[').Append((lon + dLon * Math.Cos(a)).ToString("F7", inv)).Append(',')
                .Append((lat + dLat * Math.Sin(a)).ToString("F7", inv)).Append(']');
        }
        return "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{},\"geometry\":" +
               "{\"type\":\"Polygon\",\"coordinates\":[[" + ring + "]]}}]}";
    }

    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180;
        double dp = p2 - p1, dl = (lon2 - lon1) * Math.PI / 180;
        double h = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    // Zoom at which a circle of radiusMeters spans about 300 px, so the whole range fits on a phone screen
    private static double ZoomForRadius(double lat, double radiusMeters)
    {
        double metersPerPixelAtZoom0 = 156_543.03 * Math.Cos(lat * Math.PI / 180);
        return Math.Clamp(Math.Log2(metersPerPixelAtZoom0 * 300 / (2 * radiusMeters)), 3, 19);
    }
}
