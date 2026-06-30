using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapLibreNative.Maui.Handlers;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.ViewModels;

public partial class MapViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly IGpsService _gps;

    // Accessed from StyleLoaded and toggle commands — always on main thread.
    private IMapLibreMapController? _controller;

    // Tracks which vector-tile circle layers have been added to the style so far.
    // Reset on each style reload (OnMapControllerReady).
    private readonly HashSet<string> _addedVectorLayers = new();

    private const string EmptyGeoJson = "{\"type\":\"FeatureCollection\",\"features\":[]}";

    // ── Map config ────────────────────────────────────────────────────────────
    // History-layer vector sources (per bucket, e.g. "WifiDB_weekly") are added
    // dynamically via tilejson.php when each layer is toggled on — see
    // AddVectorLayer() — not pre-baked into this base style.
    [ObservableProperty] private string _styleUrl     = "https://tiles.wifidb.net/styles/WDB_OSM/style.json";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private List<AccessPoint> _mappableAps = new();
    [ObservableProperty] private string _apsGeoJson   = EmptyGeoJson;

    // Active scanned APs = lighter than the newest WifiDB daily dots (#1aff66).
    // Inactive scanned APs = same color as daily, same base radius.
    // Both distinctions are driven by the "isActive" feature property in the GeoJSON.
    public IDictionary<string, object?> ApLayerProperties { get; } = new Dictionary<string, object?>
    {
        ["circle-radius"] = new object[] {
            "case",
            new object[] { "==", new object[] { "get", "isActive" }, true },
            4.0,  // active: slightly larger than daily (base 3.0)
            3.0,  // inactive: same base radius as daily
        },
        ["circle-color"] = new object[] {
            "case",
            new object[] { "==", new object[] { "get", "isActive" }, true },
            "#80ffb3",  // active: lighter pastel green
            "#1aff66",  // inactive: same as daily
        },
        ["circle-stroke-width"] = 0.4,
        ["circle-stroke-color"] = "#FFFFFF",
        ["circle-opacity"]      = 0.8,
    };

    // ── History layers ────────────────────────────────────────────────────────
    public ObservableCollection<HistoryLayerState> HistoryLayers { get; } = new();

    // ── Tap-to-inspect popup ──────────────────────────────────────────────────
    [ObservableProperty] private MapFeatureInfo? _selectedFeature;
    [ObservableProperty] private bool _isPopupVisible;

    public MapViewModel(IDatabaseService db, IGpsService gps)
    {
        _db  = db;
        _gps = gps;
        _gps.GpsDataReceived += OnGpsData;
        InitHistoryLayers();
    }

    private void OnGpsData(object? sender, GpsDataReceivedEventArgs e)
    {
        var d = e.GpsData;
        if (d.Quality == GpsQuality.Invalid) return;
        float bearing  = (float)(d.TrackAngle ?? 0);
        // HDOP × 10 m gives a rough horizontal accuracy estimate; cap at 5–500 m.
        float accuracy = d.HorizontalDilution.HasValue
            ? (float)Math.Clamp(d.HorizontalDilution.Value * 10.0, 5, 500)
            : 20f;
        MainThread.BeginInvokeOnMainThread(() =>
            _controller?.UpdateLocationIndicator(d.Latitude, d.Longitude, bearing, accuracy));
    }

    private static readonly string[] CellBuckets =
    {
        "cell_daily", "cell_weekly", "cell_monthly",
        "cell_0to1year", "cell_1to2year", "cell_2to3year",
        "cell_3to5year", "cell_5to10year", "cell_10yrplus",
    };

    // Canonical z-order for all history layers: newest (closest to top) → oldest.
    // ap-circles (local scan) is always above everything; hist_daily_circles is
    // pre-registered at StyleLoaded and tracked in _addedVectorLayers; all others
    // are lazy-added on toggle. BelowLayerFor() uses this to find the correct
    // insertion point regardless of toggle order.
    private static readonly string[] LayerOrder =
    [
        "hist_daily_circles",
        "hist_weekly_circles",      "hist_monthly_circles",
        "hist_0to1year_circles",    "hist_1to2year_circles",
        "hist_2to3year_circles",    "hist_3to5year_circles",
        "hist_5to10year_circles",   "hist_10yrplus_circles",
        "hist_cell_daily_circles",  "hist_cell_weekly_circles",
        "hist_cell_monthly_circles","hist_cell_0to1year_circles",
        "hist_cell_1to2year_circles","hist_cell_2to3year_circles",
        "hist_cell_3to5year_circles","hist_cell_5to10year_circles",
        "hist_cell_10yrplus_circles",
    ];

    /// Returns the id of the nearest newer layer already in the style for the
    /// given history layer, so it can be inserted below that layer.
    /// Falls back to "ap-circles" (always present above all history layers).
    private string BelowLayerFor(string layerId)
    {
        int idx = Array.IndexOf(LayerOrder, layerId);
        if (idx <= 0) return "ap-circles";
        for (int i = idx - 1; i >= 0; i--)
        {
            if (_addedVectorLayers.Contains(LayerOrder[i]))
                return LayerOrder[i];
        }
        return "ap-circles";
    }

    /// Returns the cell bucket names matching whichever wifi-age layers are
    /// currently enabled, so the Cells button mirrors the active tier set.
    private IEnumerable<string> ActiveCellBuckets()
    {
        foreach (var layer in HistoryLayers)
        {
            if (!layer.IsActive || layer.Id == "cells") continue;
            if (layer.Buckets is { } buckets)
                foreach (var b in buckets)
                    yield return "cell_" + b;
        }
    }

    // Per-bucket paint style — open/WEP/secure colors match WifiDB's map.php
    // mvt_history_layers(); radius scales from 2 (recent) to 3 (oldest).
    // Cell buckets use a single graduated purple (no sectype split, matching
    // WifiDB's CreateMvtCellLayers and VistumblerCS's CellBucketColors).
    private record BucketStyle(string Open, string Wep, string Secure, double Radius);
    private static readonly Dictionary<string, BucketStyle> BucketStyles = new()
    {
        // Radius decreases as data gets older (newest = largest + brightest).
        ["daily"]         = new("#1aff66", "#ffad33", "#ff1a1a", 3.0),
        ["weekly"]        = new("#1aff66", "#ffad33", "#ff1a1a", 3.0),
        ["monthly"]       = new("#1aff66", "#ffad33", "#ff1a1a", 3.0),
        ["0to1year"]      = new("#1aff66", "#ffad33", "#ff1a1a", 3.0),
        ["1to2year"]      = new("#00e64d", "#ff9900", "#e60000", 2.75),
        ["2to3year"]      = new("#00b33c", "#e68a00", "#cc0000", 2.5),
        ["3to5year"]      = new("#009933", "#d98000", "#c00000", 2.25),
        ["5to10year"]     = new("#00802b", "#cc7a00", "#b30000", 2.0),
        ["10yrplus"]      = new("#005c1f", "#996000", "#800000", 1.5),
        ["cell_daily"]    = new("#b296e3", "#b296e3", "#b296e3", 3.0),
        ["cell_weekly"]   = new("#9d78d8", "#9d78d8", "#9d78d8", 3.0),
        ["cell_monthly"]  = new("#885fcd", "#885fcd", "#885fcd", 3.0),
        ["cell_0to1year"] = new("#885fcd", "#885fcd", "#885fcd", 3.0),
        ["cell_1to2year"] = new("#7a4dc0", "#7a4dc0", "#7a4dc0", 2.75),
        ["cell_2to3year"] = new("#6f40b3", "#6f40b3", "#6f40b3", 2.5),
        ["cell_3to5year"] = new("#5e3599", "#5e3599", "#5e3599", 2.25),
        ["cell_5to10year"]= new("#4d2b80", "#4d2b80", "#4d2b80", 2.0),
        ["cell_10yrplus"] = new("#3d2266", "#3d2266", "#3d2266", 1.5),
    };

    // MapLibre GL zoom-interpolated radius function.
    // Holds at baseRadius from the lowest zoom up through z12 (never shrinks below
    // the per-bucket size — a curve that scales down at low zoom makes the oldest,
    // already-smallest tiers nearly invisible on a wide overview), then grows up
    // to 20px by z20 so dots stay easy to tap at street level.
    private static Dictionary<string, object?> RadiusExpr(double baseRadius) => new()
    {
        ["base"]  = 1.5,
        ["stops"] = new object[] {
            new object[] { 1,  baseRadius },
            new object[] { 12, baseRadius },
            new object[] { 20, 20.0 },
        },
    };

    // Single data-driven circle-color: a property function (no "type" — defaults to
    // Exponential) keyed on the feature's "sectype" (1=open, 2=WEP, 3=secure). sectype
    // is always exactly 1/2/3, so the implicit interpolation between stops never blends
    // colors in practice. Matches VistumblerCS's MaplibreWifiExtensions exactly.
    //
    // History: history circles failing to render was first (incorrectly) blamed entirely
    // on WifiDB's out/tiles/.htaccess emitting "Content-Encoding: gzip" on its 404 error
    // pages — a real bug, since fixed, that made a client drop no-data tiles, but not the
    // whole story. The remaining cause was in maplibre-native: a vector layer's source-layer
    // set after the layer is added (the runtime AddCircleLayer + SetSourceLayer pattern here)
    // never triggered a tile relayout, so the circles rendered nothing regardless of the
    // circle-color form. Fixed in the mln-cabi shipped with MapLibreNative.Maui 3.2.10.
    private static Dictionary<string, object?> SeCtypeStopsExpr(BucketStyle s) => new()
    {
        ["property"] = "sectype",
        ["stops"]    = new object[] {
            new object[] { 1, s.Open },
            new object[] { 2, s.Wep },
            new object[] { 3, s.Secure },
        },
    };

    private void InitHistoryLayers()
    {
        // Matches the bucket names WifiDB's mvtd daemon / tilejson.php / VistumblerCS
        // all agree on: daily, weekly, monthly, 0to1year, 1to2year, 2to3year,
        // 3to5year, 5to10year, 10yrplus (+ cell_-prefixed equivalents for Cells).
        // ActiveColor = open-network color — used as the toggle-button highlight tint.
        var defs = new HistoryLayerState[]
        {
            new() { Id = "daily",     Label = "Daily",     ActiveColor = "#1aff66", Buckets = ["daily"] },
            new() { Id = "weekly",    Label = "Weekly",    ActiveColor = "#1aff66", Buckets = ["weekly"] },
            new() { Id = "monthly",   Label = "Monthly",   ActiveColor = "#1aff66", Buckets = ["monthly"] },
            new() { Id = "0to1year",  Label = "0-1 Year",  ActiveColor = "#1aff66", Buckets = ["0to1year"] },
            new() { Id = "1to2year",  Label = "1-2 Year",  ActiveColor = "#00e64d", Buckets = ["1to2year"] },
            new() { Id = "2to3year",  Label = "2-3 Year",  ActiveColor = "#00b33c", Buckets = ["2to3year"] },
            new() { Id = "3to5year",  Label = "3-5 Year",  ActiveColor = "#009933", Buckets = ["3to5year"] },
            new() { Id = "5to10year", Label = "5-10 Year", ActiveColor = "#00802b", Buckets = ["5to10year"] },
            new() { Id = "10yrplus",  Label = "10+ Year",  ActiveColor = "#005c1f", Buckets = ["10yrplus"] },
            new() { Id = "cells",     Label = "Cell Networks", ActiveColor = "#885fcd", Buckets = CellBuckets },
        };

        foreach (var layer in defs)
        {
            var captured = layer;
            layer.ToggleCommand = new AsyncRelayCommand(
                () => ToggleHistoryLayerAsync(captured),
                () => !captured.IsLoading);
            HistoryLayers.Add(layer);
        }
    }

    // ── Controller wiring ─────────────────────────────────────────────────────

    /// <summary>
    /// Called from MapPage.xaml.cs exactly once per style load, from the StyleLoaded event.
    ///
    /// All history layers (including daily) are MVT vector layers served by WifiDB's mvtd
    /// daemon via tilejson.php?bucket={bucket}, exactly like VistumblerCS — each is lazily
    /// added/removed on toggle (see AddVectorLayer / RemoveVectorLayer). This relies on the
    /// mln-cabi fix shipped in MapLibreNative.Maui 3.2.10 that makes a vector layer's
    /// source-layer trigger a tile relayout when set after the layer is added; before that
    /// fix, runtime vector circle layers rendered nothing, which is why daily previously used
    /// a GeoJSON source as a workaround.
    ///
    /// Nothing is pre-registered here on a fresh load; this only re-applies layers that were
    /// already toggled on before a style reload, in LayerOrder so each finds its belowLayerId.
    /// All controller calls run on the main thread inside the StyleLoaded callback.
    /// </summary>
    public void OnMapControllerReady(IMapLibreMapController controller)
    {
        _controller = controller;
        _addedVectorLayers.Clear();

        foreach (var layer in HistoryLayers
            .Where(l => l.IsActive)
            .OrderBy(l => l.Id == "cells" ? int.MaxValue   // cells last
                : Array.IndexOf(LayerOrder,
                    l.Buckets?.Length > 0 ? $"hist_{l.Buckets[0]}_circles" : "")))
        {
            if (layer.Id == "cells") AddCellLayers();
            else                     AddVectorLayer(layer);
        }
    }

    // ── Live AP source ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadMappableApsAsync()
    {
        await _db.InitializeAsync();
        var all = await _db.GetAllAccessPointsAsync();
        MappableAps  = all.Where(a => a.Latitude.HasValue && a.Longitude.HasValue).ToList();
        ApsGeoJson   = BuildGeoJson(MappableAps);
        StatusMessage = $"{MappableAps.Count} scanned APs with GPS";
    }

    // ── History layer toggle ──────────────────────────────────────────────────

    private Task ToggleHistoryLayerAsync(HistoryLayerState layer)
    {
        if (_controller is null)
        {
            StatusMessage = "Map not ready — wait for style to load";
            return Task.CompletedTask;
        }

        layer.IsActive = !layer.IsActive;

        if (layer.Id == "cells")
        {
            // Only activate cell sub-buckets for the wifi-age tiers currently visible,
            // matching WifiDB's web-map behaviour.
            if (layer.IsActive) AddCellLayers();
            else                RemoveAllCellLayers();
        }
        else
        {
            if (layer.IsActive)
                AddVectorLayer(layer);
            else
                RemoveVectorLayer(layer);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Add a per-bucket vector source + circle layer for every bucket this layer
    /// covers. The source is loaded directly from tilejson.php (same as the WifiDB
    /// web map's mvtd-backed buttons) rather than assuming it's pre-baked into the
    /// style — MapLibre auto-discovers the real tile URL template/zoom range from
    /// the fetched TileJSON document.
    /// </summary>
    private void AddVectorLayer(HistoryLayerState layer)
    {
        if (_controller is null || layer.Buckets is null) return;

        foreach (var bucket in layer.Buckets)
        {
            var layerId = $"hist_{bucket}_circles";
            if (_addedVectorLayers.Contains(layerId)) continue;

            var sourceId = $"WifiDB_{bucket}";
            _controller.AddVectorSource(
                sourceName:       sourceId,
                tileUrl:          $"https://wifidb.net/api/tilejson.php?bucket={bucket}",
                tileUrlTemplates: null,
                minZoom: 0, maxZoom: 22);

            var style = BucketStyles.TryGetValue(bucket, out var s) ? s : new BucketStyle(layer.ActiveColor, layer.ActiveColor, layer.ActiveColor, 2.5);
            _controller.AddCircleLayer(
                layerName:    layerId,
                sourceName:   sourceId,
                belowLayerId: BelowLayerFor(layerId),
                sourceLayer:  bucket,
                properties: new Dictionary<string, object?>
                {
                    ["circle-radius"]  = RadiusExpr(style.Radius),
                    ["circle-color"]   = SeCtypeStopsExpr(style),
                    ["circle-opacity"] = 1.0,
                    ["circle-blur"]    = 0.5,
                });

            _addedVectorLayers.Add(layerId);
        }
        StatusMessage = $"{layer.Label} layer on";
    }

    private void RemoveVectorLayer(HistoryLayerState layer)
    {
        if (_controller is null || layer.Buckets is null) return;

        foreach (var bucket in layer.Buckets)
        {
            var layerId = $"hist_{bucket}_circles";
            _controller.RemoveLayer(layerId);
            _addedVectorLayers.Remove(layerId);
        }
        StatusMessage = $"{layer.Label} layer off";
    }

    /// Adds cell layers for whichever wifi-age tiers are currently active,
    /// matching the WifiDB web-map "Cell Networks" button behaviour. Cell towers
    /// use `type` (LTE/GSM/etc.), not `sectype`, so a single literal (non-data-driven)
    /// color is used per bucket — no filtering needed.
    private void AddCellLayers()
    {
        if (_controller is null) return;
        foreach (var bucket in ActiveCellBuckets())
        {
            var layerId  = $"hist_{bucket}_circles";
            if (_addedVectorLayers.Contains(layerId)) continue;

            var sourceId = $"WifiDB_{bucket}";
            _controller.AddVectorSource(
                sourceName: sourceId,
                tileUrl:    $"https://wifidb.net/api/tilejson.php?bucket={bucket}",
                tileUrlTemplates: null, minZoom: 0, maxZoom: 22);

            var style = BucketStyles.TryGetValue(bucket, out var s) ? s : BucketStyles["cell_monthly"];
            _controller.AddCircleLayer(
                layerName:    layerId,
                sourceName:   sourceId,
                belowLayerId: BelowLayerFor(layerId),
                sourceLayer:  bucket,
                properties: new Dictionary<string, object?>
                {
                    ["circle-radius"]  = RadiusExpr(style.Radius),
                    ["circle-color"]   = style.Open,   // cells: Open/Wep/Secure are all equal
                    ["circle-opacity"] = 1.0,
                    ["circle-blur"]    = 0.5,
                });
            _addedVectorLayers.Add(layerId);
        }
        StatusMessage = "Cells layer on";
    }

    /// Removes all currently-visible cell layers regardless of which tiers they cover.
    private void RemoveAllCellLayers()
    {
        if (_controller is null) return;
        foreach (var layerId in _addedVectorLayers
            .Where(l => l.StartsWith("hist_cell_")).ToList())
        {
            _controller.RemoveLayer(layerId);
            _addedVectorLayers.Remove(layerId);
        }
        StatusMessage = "Cells layer off";
    }

    // ── Tap-to-inspect popup ──────────────────────────────────────────────────

    /// <summary>
    /// Called from MapPage.xaml.cs when the user taps the map. Hit-tests every
    /// currently-visible marker layer (live scan + any active history layers) in
    /// priority order, builds a normalized <see cref="MapFeatureInfo"/> from
    /// whichever one matched first, and merges in the local DB record if this
    /// BSSID has ever been scanned by this device.
    /// </summary>
    public async Task OnMapTappedAsync(double screenX, double screenY)
    {
        if (_controller is null) return;

        // Query layers in priority order: live scan first, then daily, then whatever
        // vector history layers are currently toggled on. Querying one layer at a
        // time (rather than a combined comma-list) lets us know exactly which
        // layer/source schema produced the hit, since the different tile layers
        // use different property names (bssid vs mac, channel vs chan, etc).
        var candidateLayers = new List<(string LayerId, string SourceLabel)> { ("ap-circles", "Live scan") };
        foreach (var layer in HistoryLayers)
        {
            if (!layer.IsActive) continue;
            if (layer.Buckets is { } buckets)
                foreach (var bucket in buckets)
                    candidateLayers.Add(($"hist_{bucket}_circles", layer.Label));
        }

        foreach (var (layerId, sourceLabel) in candidateLayers)
        {
            string? json;
            try { json = _controller.QueryRenderedFeaturesAtPoint(screenX, screenY, layerId); }
            catch { continue; } // layer not present in the style yet — try the next one

            var info = ParseFirstFeature(json, sourceLabel);
            if (info is null) continue;

            if (!string.IsNullOrWhiteSpace(info.Bssid))
            {
                var local = await _db.GetAccessPointByBssidAsync(info.Bssid);
                if (local is not null)
                {
                    info.HasLocalRecord = true;
                    if (string.IsNullOrWhiteSpace(info.Ssid)) info.Ssid = local.Ssid;
                    info.Manufacturer = string.IsNullOrWhiteSpace(local.Manufacturer) ? info.Manufacturer : local.Manufacturer;
                    info.FirstSeen = local.FirstSeen == default ? info.FirstSeen : local.FirstSeen.ToString("yyyy-MM-dd HH:mm");
                    info.LastSeen = local.LastSeen == default ? info.LastSeen : local.LastSeen.ToString("yyyy-MM-dd HH:mm");
                }
            }

            SelectedFeature = info;
            IsPopupVisible = true;
            return;
        }

        // Nothing under the tap — dismiss any open popup.
        IsPopupVisible = false;
    }

    [RelayCommand]
    private void ClosePopup() => IsPopupVisible = false;

    [RelayCommand]
    private async Task ViewInApListAsync()
    {
        var bssid = SelectedFeature?.Bssid;
        if (string.IsNullOrWhiteSpace(bssid)) return;

        IsPopupVisible = false;
        await Shell.Current.GoToAsync($"//ScanPage?bssid={Uri.EscapeDataString(bssid)}");
    }

    /// <summary>
    /// Parses the first Feature out of a QueryRenderedFeaturesAtPoint GeoJSON
    /// FeatureCollection and normalizes its properties — live-scan features use
    /// ssid/bssid/signal/channel; wifidb.net vector tiles use ssid/mac/auth/chan
    /// (WiFi) or ssid/mac/authmode/chan (cell), per api/mvt.php's $enc_keys.
    /// </summary>
    private static MapFeatureInfo? ParseFirstFeature(string? geoJson, string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(geoJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            if (!doc.RootElement.TryGetProperty("features", out var features) ||
                features.ValueKind != JsonValueKind.Array || features.GetArrayLength() == 0)
                return null;

            var feature = features[0];
            if (!feature.TryGetProperty("properties", out var props))
                return null;

            return new MapFeatureInfo
            {
                SourceLabel    = sourceLabel,
                Bssid          = GetProp(props, "bssid", "mac") ?? "",
                Ssid           = GetProp(props, "ssid") ?? "",
                Authentication = GetProp(props, "auth", "authmode") ?? "",
                Encryption     = GetProp(props, "encry") ?? "",
                Channel        = GetProp(props, "channel", "chan") ?? "",
                RadioType      = GetProp(props, "radio", "nt", "type") ?? "",
                Manufacturer   = GetProp(props, "manuf") ?? "",
                Signal         = GetProp(props, "signal", "high_gps_sig") ?? "",
                Rssi           = GetProp(props, "rssi", "high_gps_rssi") ?? "",
                FirstSeen      = GetProp(props, "fa") ?? "",
                LastSeen       = GetProp(props, "la") ?? "",
                UploadedBy     = GetProp(props, "user") ?? "",
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetProp(JsonElement props, params string[] names)
    {
        foreach (var name in names)
        {
            if (!props.TryGetProperty(name, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;

            var s = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildGeoJson(List<AccessPoint> aps)
    {
        if (aps.Count == 0) return EmptyGeoJson;
        var features = aps.Select(ap =>
        {
            var ssid   = ap.Ssid?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
            var bssid  = ap.Bssid?.Replace("\"", "\\\"") ?? "";
            var active = ap.IsActive ? "true" : "false";
            return $"{{\"type\":\"Feature\","
                 + $"\"geometry\":{{\"type\":\"Point\",\"coordinates\":[{ap.Longitude:F6},{ap.Latitude:F6}]}},"
                 + $"\"properties\":{{\"ssid\":\"{ssid}\",\"bssid\":\"{bssid}\","
                 + $"\"signal\":{ap.Signal ?? 0},\"channel\":{ap.Channel},\"isActive\":{active}}}}}";
        });
        return $"{{\"type\":\"FeatureCollection\",\"features\":[{string.Join(",", features)}]}}";
    }
}

