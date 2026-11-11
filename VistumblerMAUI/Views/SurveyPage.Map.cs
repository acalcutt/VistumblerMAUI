using System.Text.Json;
using MapLibreNative.Maui.Handlers;
using MapLibreNative.Maui.Handlers.EventArgs;
using MapLibreNative.Maui.Handlers.Geometry;
using VistumblerMAUI.Services.Survey;
using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

/// <summary>
/// The survey over the map: a MapLibre map with the Map tab's basemap, the plan image laid on it at its corners,
/// and the spots as a circle layer. The map is made the first time the map view is shown, and redraws its layers
/// after every style load, as the Map tab does.
/// </summary>
public partial class SurveyPage
{
    private const string PlanSource = "survey-plan", PlanLayer = "survey-plan";
    private const string SpotsSource = "survey-spots", SpotsLayer = "survey-spots";

    private MapLibreMap? _map;
    private bool _styleLoaded, _cameraPlaced;

    private IMapLibreMapController? Controller => (_map?.Handler as MapLibreMapHandler)?.Controller;

    private void OnMapViewChanged()
    {
        if (!_vm.IsMapView) return;
        EnsureMap();
        if (_styleLoaded) PlaceCamera();
    }

    // Makes the map the first time the map view is shown, so the plan alone never pays for one
    private void EnsureMap()
    {
        if (_map is not null) return;
        _map = new MapLibreMap
        {
            StyleUrl = Services.MapStyles.StyleUrl,
            ShowNavigationControls = true,
            ShowGpsControl = true,
            ShowAttributionControl = true,
            MyLocationEnabled = true,
        };
#if ANDROID
        // As the Map tab: honour the OS font size, which MapLibre doesn't on its own
        float fontScale = Android.App.Application.Context.Resources?.Configuration?.FontScale ?? 1f;
        _map.UiScale = Math.Clamp(fontScale, 0.5, 2.0);
#endif
        _map.MapReady += (_, _) =>
        {
            // As the Map tab: on Windows the style set before the native map exists is lost; set it again
            try { Controller?.SetStyleString(Services.MapStyles.StyleUrl); }
            catch (Exception ex) { Services.DebugLog.Write($"[Survey] SetStyleString: {ex.Message}"); }
        };
        _map.StyleLoaded += (_, _) =>
        {
            _styleLoaded = true;
            DrawPlanOnMap();
            PlaceCamera();
        };
        _map.MapClick += OnMapClick;
        MapHost.Content = _map;
    }

    /// <summary>
    /// The first time the map shows a survey: over the plan or the spots, if they're on the map; otherwise where the
    /// Map tab was looking, so a plan to line up starts somewhere familiar.
    /// </summary>
    private void PlaceCamera()
    {
        if (_cameraPlaced || Controller is not { } ctrl) return;
        _cameraPlaced = true;
        try
        {
            if (_vm.MapExtent() is { } e)
            {
                // A little room around it, and never closer than a building
                double lat = Math.Max((e.North - e.South) * 1.3, 0.0008), lon = Math.Max((e.East - e.West) * 1.3, 0.0008);
                ctrl.JumpTo(new MapLibreNative.Maui.Geometry.MapSpan(
                    new MapLibreNative.Maui.Geometry.MapCoordinate((e.North + e.South) / 2, (e.East + e.West) / 2), lat, lon));
            }
            else if (_mapTab.CurrentCamera() is { } cam)
            {
                ctrl.JumpTo(cam.Lat, cam.Lon, cam.Zoom);
            }
        }
        catch (Exception ex)
        {
            Services.DebugLog.Write($"[Survey] camera: {ex.Message}");
        }
    }

    /// <summary>The plan image on the map at its corners (not while it's being lined up), then the spots on top.</summary>
    private void DrawPlanOnMap()
    {
        if (!_styleLoaded || Controller is not { } ctrl) return;
        try
        {
            ctrl.RemoveLayer(PlanLayer);
            ctrl.RemoveSource(PlanSource);
            if (_vm.PlanCorners is { } c && _vm.PlanImagePath is { } path && !_vm.IsPlacingPlan)
            {
                // MapLibre opens what follows file:// as the path, so "file://C:/..." on Windows and "file:///data/..." on
                // Android; .NET's file:///C:/... would leave "/C:/...", which Windows can't open
                AddPlanSource(ctrl, "file://" + path.Replace('\\', '/'), c);
                ctrl.AddRasterLayer(PlanLayer, PlanSource, new Dictionary<string, object?>
                {
                    ["raster-opacity"] = 0.85,   // the map's streets and buildings show through a little
                    ["raster-fade-duration"] = 0,
                });
            }
        }
        catch (Exception ex)
        {
            Services.DebugLog.Write($"[Survey] plan on map: {ex.Message}");
        }
        DrawSpotsOnMap();
    }

    /// <summary>
    /// The plan image as a MapLibre image source at its corners, written as the style spec has it (corners top-left,
    /// top-right, bottom-right, bottom-left, each [lon, lat]). Not through the controller's AddImageSource: in
    /// MapLibre MAUI 5.0.0-pre.3 that passes the top corners swapped on Windows and drops the corners on Android and
    /// iOS. Windows and iOS controllers take a source's JSON; Android's doesn't expose it, so there it's given to
    /// the style the controller keeps.
    /// </summary>
    private static void AddPlanSource(IMapLibreMapController ctrl, string url, PlanCorners c)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "image",
            url,
            coordinates = new[]
            {
                new[] { c.TopLeft.Lon, c.TopLeft.Lat }, new[] { c.TopRight.Lon, c.TopRight.Lat },
                new[] { c.BottomRight.Lon, c.BottomRight.Lat }, new[] { c.BottomLeft.Lon, c.BottomLeft.Lat },
            },
        });
#if ANDROID
        var style = ctrl.GetType().GetField("_style", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(ctrl) as MapLibreNative.Maui.MlnStyle;
        if (style is null) throw new InvalidOperationException("the map's style isn't reachable");
        style.AddSourceJson(PlanSource, json);
#else
        ((MapLibreMapController)ctrl).AddSourceJson(PlanSource, json);
#endif
    }

    /// <summary>The spots, coloured as on the plan, with the one being measured as a blue ring. Kept on top.</summary>
    private void DrawSpotsOnMap()
    {
        if (!_styleLoaded || Controller is not { } ctrl) return;
        try
        {
            ctrl.RemoveLayer(SpotsLayer);
            ctrl.SetGeoJsonFeature(SpotsSource, _vm.SpotsGeoJson());
            var pending = new object[] { "has", "pending" };
            ctrl.AddCircleLayer(SpotsLayer, SpotsSource, belowLayerId: null, sourceLayer: null, properties: new Dictionary<string, object?>
            {
                ["circle-radius"] = new object[] { "case", pending, 12.0, 8.0 },
                ["circle-color"] = new object[] { "get", "color" },
                ["circle-opacity"] = new object[] { "case", pending, 0.0, 1.0 },
                ["circle-stroke-color"] = new object[] { "case", pending, "#1565C0", "#FFFFFF" },
                ["circle-stroke-width"] = new object[] { "case", pending, 3.0, 2.0 },
            });
        }
        catch (Exception ex)
        {
            Services.DebugLog.Write($"[Survey] spots on map: {ex.Message}");
        }
    }

    // A tap on the map: on a spot, that spot; elsewhere, measure there. Ignored while lining the plan up.
    private async void OnMapClick(object? sender, MapClickEventArgs e)
    {
        if (_vm.IsPlacingPlan || Controller is not { } ctrl) return;
        try
        {
            int hit = -1;
            try
            {
                if (ctrl.QueryRenderedFeaturesAtPoint(e.ScreenX, e.ScreenY, SpotsLayer) is { Length: > 0 } json)
                    hit = FirstIndex(json);
            }
            catch { /* no spots layer yet */ }
            var at = ctrl.ScreenPointToLatLng(e.ScreenX, e.ScreenY);
            await _vm.MapTapAsync(at.Latitude, at.Longitude, hit);
        }
        catch (Exception ex)
        {
            Services.DebugLog.Write($"[Survey] map tap: {ex.Message}");
        }
    }

    // The "index" of the first spot in a feature query's result: a FeatureCollection, or a bare array of features
    private static int FirstIndex(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var features = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
            : doc.RootElement.TryGetProperty("features", out var f) ? f : default;
        if (features.ValueKind != JsonValueKind.Array) return -1;
        foreach (var feature in features.EnumerateArray())
            if (feature.TryGetProperty("properties", out var p) && p.TryGetProperty("index", out var i) && i.TryGetInt32(out var index) && index >= 0)
                return index;
        return -1;
    }

    /// <summary>
    /// Pin here: reads the map positions under the corners of the image as it's shown over the map. The image is
    /// drawn fitted (letterboxed) inside its frame, so its corners are found from its size; they're then converted
    /// from the page's units to the map's screen units, which <see cref="CalibrateScreen"/> measures.
    /// </summary>
    private void OnPinHere(object? sender, EventArgs e)
    {
        if (Controller is not { } ctrl || _map is null) return;
        double frameW = PlaceImage.Width, frameH = PlaceImage.Height;
        if (frameW <= 0 || frameH <= 0 || _vm.PlanWidth <= 0 || _vm.PlanHeight <= 0) return;

        double fit = Math.Min(frameW / _vm.PlanWidth, frameH / _vm.PlanHeight);
        double w = _vm.PlanWidth * fit, h = _vm.PlanHeight * fit;
        // The image's top-left on the map view, in page units (both fill the same grid cell)
        double left = PlaceImage.X - MapHost.X + (frameW - w) / 2;
        double top = PlaceImage.Y - MapHost.Y + (frameH - h) / 2;

        double k = CalibrateScreen(ctrl);
        GeoPoint At(double x, double y)
        {
            var p = ctrl.ScreenPointToLatLng(x * k, y * k);
            return new GeoPoint(p.Latitude, p.Longitude);
        }
        _vm.PinPlan(new PlanCorners
        {
            TopLeft = At(left, top),
            TopRight = At(left + w, top),
            BottomRight = At(left + w, top + h),
            BottomLeft = At(left, top + h),
        });
    }

    /// <summary>
    /// How many of the map's screen units make one of the page's: the map works in its own (pixels on some
    /// platforms, scaled by the font-size setting on Android), so it's measured from where the map's centre falls.
    /// </summary>
    private double CalibrateScreen(IMapLibreMapController ctrl)
    {
        try
        {
            var center = ctrl.GetCenter();
            var (sx, sy) = ctrl.LatLngToScreenPoint(center.Latitude, center.Longitude);
            double kx = _map!.Width > 0 ? sx / (_map.Width / 2) : 1;
            double ky = _map.Height > 0 ? sy / (_map.Height / 2) : 1;
            double k = (kx + ky) / 2;
            return double.IsFinite(k) && k > 0.1 && k < 10 ? k : 1;
        }
        catch
        {
            return 1;
        }
    }

    // The plan or where it sits changed: show it (or the image to line up) again
    private void OnPlanChanged()
    {
        PlaceImage.Source = _vm.IsPlacingPlan && _vm.PlanImagePath is { } path ? ImageSource.FromFile(path) : null;
        if (_vm.IsPlacingPlan) OnMapViewChanged();
        DrawPlanOnMap();
    }
}
