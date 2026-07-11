using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapLibreNative.Maui;
using MapLibreNative.Maui.Handlers;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.ViewModels;

public partial class MapViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly IGpsService _gps;
    private readonly ScanViewModel _scan;

    // Accessed from StyleLoaded and toggle commands — always on main thread.
    private IMapLibreMapController? _controller;
    // Tracks which vector-tile circle layers have been added to the style so far.
    // Reset on each style reload (OnMapControllerReady).
    private readonly HashSet<string> _addedVectorLayers = new();

    // Lazily created the first time the user pre-caches a map area for offline use.
    // Shares the map's cache database (MbglCache.DefaultPath), so downloaded tiles are
    // served straight to the live map — including when the network is forced offline.
    private MbglOfflineManager? _offline;

    private const string EmptyGeoJson = "{\"type\":\"FeatureCollection\",\"features\":[]}";

    // ── Map config ────────────────────────────────────────────────────────────
    // History-layer vector sources (per bucket, e.g. "WifiDB_weekly") are added
    // dynamically via tilejson.php when each layer is toggled on — see
    // AddVectorLayer() — not pre-baked into this base style.
    [ObservableProperty] private string _styleUrl     = MapStyles.StyleUrl;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private List<AccessPoint> _mappableAps = new();
    [ObservableProperty] private string _apsGeoJson   = EmptyGeoJson;

    // ── Offline mode ──────────────────────────────────────────────────────────
    // True while MapLibre is forced offline (serving only cached tiles). Drives the
    // "Save Area / Go Offline·Go Online" toolbar items on MapPage.
    [ObservableProperty] private bool _isOffline;
    public string OfflineToggleLabel => IsOffline ? "Go Online" : "Go Offline";
    partial void OnIsOfflineChanged(bool value) => OnPropertyChanged(nameof(OfflineToggleLabel));

    // Live-scan circle paint. Color is per-sectype (open/WEP/secure) AND active/dead:
    // each feature carries a "styidx" folding both into one 1..6 value (active 1/2/3,
    // dead 4/5/6), so active APs render in the brightest configured colors and this
    // session's dead APs in their dimmer variants. Radius keeps active dots a touch
    // larger. Rebuilt from MapColors by BuildApLayerProperties() / RefreshMapColors().
    [ObservableProperty] private IDictionary<string, object?> _apLayerProperties = new Dictionary<string, object?>();

    private void BuildApLayerProperties()
    {
        var (aOpen, aWep, aSec) = MapColors.Hashed("live_active");
        var (dOpen, dWep, dSec) = MapColors.Hashed("live_dead");
        ApLayerProperties = new Dictionary<string, object?>
        {
            // Zoom-interpolated like the history buckets' RadiusExpr — a fixed 4px dot is
            // near-invisible on a high-DPI phone at street zoom. Holds the base size
            // through z12, then grows to ~22px by z20 so dots stay easy to see and tap.
            // Active APs stay a step larger than dead ones at every zoom.
            ["circle-radius"] = new object[] {
                "interpolate", new object[] { "exponential", 1.5 }, new object[] { "zoom" },
                1.0,  new object[] { "case", new object[] { "==", new object[] { "get", "isActive" }, true }, 4.0, 3.0 },
                12.0, new object[] { "case", new object[] { "==", new object[] { "get", "isActive" }, true }, 4.0, 3.0 },
                20.0, new object[] { "case", new object[] { "==", new object[] { "get", "isActive" }, true }, 22.0, 18.0 },
            },
            ["circle-color"] = new Dictionary<string, object?> {
                ["property"] = "styidx",
                ["stops"]    = new object[] {
                    new object[] { 1, aOpen }, new object[] { 2, aWep }, new object[] { 3, aSec },
                    new object[] { 4, dOpen }, new object[] { 5, dWep }, new object[] { 6, dSec },
                },
            },
            ["circle-stroke-width"] = 0.4,
            ["circle-stroke-color"] = "#FFFFFF",
            ["circle-opacity"]      = 0.8,
        };
    }

    // ── History layers ────────────────────────────────────────────────────────
    public ObservableCollection<HistoryLayerState> HistoryLayers { get; } = new();

    // ── Tap-to-inspect popup ──────────────────────────────────────────────────
    [ObservableProperty] private MapFeatureInfo? _selectedFeature;
    [ObservableProperty] private bool _isPopupVisible;

    // ── GPS track (live breadcrumb line) ──────────────────────────────────────
    // Mirrors the WifiDB web map's "Enable Track" feature: while enabled, every GPS
    // fix is appended to a LineString drawn as a yellow line on the map. Points live
    // here (singleton VM) so the track survives page navigation and style reloads.
    [ObservableProperty] private bool _isTrackEnabled;
    public string TrackToggleLabel => IsTrackEnabled ? "Disable Track" : "Enable Track";
    partial void OnIsTrackEnabledChanged(bool value) => OnPropertyChanged(nameof(TrackToggleLabel));

    // The track is a list of line segments rather than one line: a long silence
    // between fixes (screen off without the keep-alive service, GPS dropout) means
    // the path in between is unknown, so a new segment starts instead of drawing a
    // straight connector across the gap. Same 180 s rule as the KML export.
    private const double TrackGapSeconds = 180;
    private readonly List<List<(double Lon, double Lat)>> _trackSegments = new();
    private DateTime _lastTrackFixUtc;
    private bool _trackLayerAdded;

    // Deliberately independent of the session DB's GPS history: this is a live
    // "since Enable was tapped" breadcrumb (like the WifiDB web map's track), while
    // the DB rows remain the durable session record that KML/GPX exports draw from.
    [RelayCommand]
    private void ToggleTrack()
    {
        IsTrackEnabled = !IsTrackEnabled;
        if (IsTrackEnabled) EnsureTrackLayer();
        StatusMessage = IsTrackEnabled ? "Track recording on" : "Track recording off";
    }

    /// <summary>Display-only: wipes the drawn line. The recorded GPS history in the
    /// session database (and therefore KML/GPX exports) is untouched.</summary>
    [RelayCommand]
    private void ClearTrack()
    {
        _trackSegments.Clear();
        if (_trackLayerAdded) _controller?.SetGeoJsonSource("track-source", TrackGeoJson());
        StatusMessage = "Track cleared (exports keep full history)";
    }

    private string TrackGeoJson()
    {
        // A LineString requires ≥2 positions; an empty/1-point one is invalid GeoJSON
        // that the native parser rejects — leaving the previously rendered track on
        // screen (Clear Track looked like it "didn't work"). Only segments with two
        // or more points become features; with none, send a valid empty collection.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var features = string.Join(",", _trackSegments
            .Where(s => s.Count >= 2)
            .Select(s => "{\"type\":\"Feature\",\"geometry\":{\"type\":\"LineString\","
                       + "\"coordinates\":[" + string.Join(",", s.Select(p =>
                             $"[{p.Lon.ToString("F6", inv)},{p.Lat.ToString("F6", inv)}]"))
                       + "]},\"properties\":{}}"));
        return features.Length == 0
            ? EmptyGeoJson
            : "{\"type\":\"FeatureCollection\",\"features\":[" + features + "]}";
    }

    /// <summary>Add the track source + line layer to the current style (once per style).</summary>
    private void EnsureTrackLayer()
    {
        if (_controller is null || _trackLayerAdded) return;
        try
        {
            _controller.AddGeoJsonSource("track-source", TrackGeoJson());
            // Dark casing under a fully-opaque bright yellow line — pale yellow alone
            // washes out against the light basemap. Both layers share the one source,
            // so per-fix SetGeoJsonSource updates redraw them together.
            _controller.AddLineLayer("track-line-casing", "track-source", belowLayerId: null,
                sourceLayer: null, properties: new Dictionary<string, object?>
                {
                    ["line-cap"]     = "round",
                    ["line-join"]    = "round",
                    ["line-color"]   = "#000000",
                    ["line-width"]   = 8.0,
                    ["line-opacity"] = 0.4,
                });
            _controller.AddLineLayer("track-line", "track-source", belowLayerId: null,
                sourceLayer: null, properties: new Dictionary<string, object?>
                {
                    ["line-cap"]     = "round",
                    ["line-join"]    = "round",
                    ["line-color"]   = "#FFE600",
                    ["line-width"]   = 5.0,
                    ["line-opacity"] = 1.0,
                });
            _trackLayerAdded = true;
        }
        catch { /* style not ready yet — retried on the next fix / style reload */ }
    }

    public MapViewModel(IDatabaseService db, IGpsService gps, ScanViewModel scan)
    {
        _db   = db;
        _gps  = gps;
        _scan = scan;
        _gps.GpsDataReceived += OnGpsData;
        InitHistoryLayers();
        RefreshMapColors();
    }

    // ── Map colors ──────────────────────────────────────────────────────────────
    // Revision of MapColors that the live layer + bucket styles currently reflect.
    // MapPage compares this against MapColors.Revision on appearing and reloads the
    // map when the user has changed colors in Settings.
    public int AppliedColorRevision { get; private set; }

    /// <summary>Rebuild the live-layer paint and per-bucket styles from the current MapColors.</summary>
    public void RefreshMapColors()
    {
        LoadBucketStyles();
        BuildApLayerProperties();
        AppliedColorRevision = MapColors.Revision;
    }

    /// <summary>Clear the plotted APs when switching to a new session (the live timer will
    /// then reload from the new, empty session database).</summary>
    public void ResetForNewSession()
    {
        MappableAps   = new List<AccessPoint>();
        ApsGeoJson    = EmptyGeoJson;
        StatusMessage = "New session";
    }

    private void OnGpsData(object? sender, GpsDataReceivedEventArgs e)
    {
        var d = e.GpsData;
        if (d.Quality == GpsQuality.Invalid) return;
        float bearing  = (float)(d.TrackAngle ?? 0);
        // Prefer the source's reported horizontal accuracy (metres). Otherwise fall
        // back to an HDOP × 10 m estimate, then a conservative default. Cap at 5–500 m.
        double accuracyMeters =
              d.Accuracy.HasValue           ? d.Accuracy.Value
            : d.HorizontalDilution.HasValue ? d.HorizontalDilution.Value * 10.0
            :                                 20.0;
        float accuracy = (float)Math.Clamp(accuracyMeters, 5, 500);
        // Feed the GPS control overlay. Its 4-state button (Off / Show / Follow /
        // FollowBearing) decides whether the location puck is drawn and whether the
        // camera follows the fix — tapping the on-map GPS button cycles the mode.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _controller?.UpdateGpsLocation(d.Latitude, d.Longitude, bearing, accuracy);

            // Append to the breadcrumb track only after real movement. Stationary GPS
            // jitter wobbles a few metres between fixes, which would otherwise grow the
            // track by a point every second without adding any visible line — so skip
            // fixes within ~5 m of the last recorded point (below typical fix accuracy).
            if (IsTrackEnabled)
            {
                var now = DateTime.UtcNow;
                bool gap = _trackSegments.Count > 0 &&
                           (now - _lastTrackFixUtc).TotalSeconds > TrackGapSeconds;
                if (_trackSegments.Count == 0 || gap)
                    _trackSegments.Add(new List<(double Lon, double Lat)>());
                _lastTrackFixUtc = now;   // every fix counts as "GPS alive", added or not

                var seg = _trackSegments[^1];
                // Movement threshold scales with the fix's reported accuracy: a tight
                // fix (2-3 m) records fine detail so corners stay smooth, a loose fix
                // demands more movement so stationary jitter doesn't scribble. Clamped
                // to 2-10 m.
                double minMoveMeters = Math.Clamp((d.Accuracy ?? 10.0) * 0.5, 2.0, 10.0);
                bool moved = seg.Count == 0;
                if (!moved)
                {
                    var (lon0, lat0) = seg[^1];
                    double dLat = (d.Latitude  - lat0) * 111_320.0;
                    double dLon = (d.Longitude - lon0) * 111_320.0 * Math.Cos(lat0 * Math.PI / 180.0);
                    moved = dLat * dLat + dLon * dLon >= minMoveMeters * minMoveMeters;
                }
                if (moved)
                {
                    seg.Add((d.Longitude, d.Latitude));
                    EnsureTrackLayer();
                    if (_trackLayerAdded)
                        _controller?.SetGeoJsonSource("track-source", TrackGeoJson());
                }
            }
        });
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

    // Per-bucket paint style. Wifi open/WEP/secure colors come from MapColors (the Map
    // settings tab) with a brightness gradient that darkens with age; radius scales from
    // 3 (recent) down to 1.5 (oldest). Cell buckets use a single graduated purple (no
    // sectype split) and are not user-configurable. Rebuilt by LoadBucketStyles().
    private record BucketStyle(string Open, string Wep, string Secure, double Radius);

    // Wifi history bucket radii (colors are pulled from MapColors); newest = largest.
    private static readonly (string Bucket, double Radius)[] WifiBucketRadii =
    {
        ("daily", 3.0), ("weekly", 3.0), ("monthly", 3.0), ("0to1year", 3.0),
        ("1to2year", 2.75), ("2to3year", 2.5), ("3to5year", 2.25),
        ("5to10year", 2.0), ("10yrplus", 1.5),
    };

    private static readonly Dictionary<string, BucketStyle> CellBucketStyles = new()
    {
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

    // Combined wifi (from MapColors) + cell (fixed) styles; rebuilt on color change.
    private Dictionary<string, BucketStyle> _bucketStyles = new();

    private void LoadBucketStyles()
    {
        var d = new Dictionary<string, BucketStyle>(CellBucketStyles);
        foreach (var (bucket, radius) in WifiBucketRadii)
        {
            var (o, w, s) = MapColors.Hashed(bucket);
            d[bucket] = new BucketStyle(o, w, s, radius);
        }
        _bucketStyles = d;
    }

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

        // Fresh style — the track layer (if any) was lost with the old style; re-add it
        // so an in-progress track keeps drawing after navigation/style changes.
        _trackLayerAdded = false;
        if (IsTrackEnabled || _trackSegments.Count > 0)
            EnsureTrackLayer();

        // Seed the (possibly brand-new) native controller with the latest known fix.
        // A stationary device may not raise another LocationChanged for a long time,
        // so without this the puck/follow modes stay dormant on a freshly-opened map
        // until the user restarts GPS (whose StartAsync publishes a cached fix).
        if (_gps.IsActive && _gps.CurrentGpsData is { } d && d.Quality != GpsQuality.Invalid)
            OnGpsData(this, new GpsDataReceivedEventArgs { GpsData = d });

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

    // ── Offline map caching ───────────────────────────────────────────────────

    /// <summary>Lazily creates the shared offline manager and routes its progress/error
    /// callbacks (raised on MapLibre's database thread) to <see cref="StatusMessage"/>.</summary>
    private MbglOfflineManager GetOfflineManager()
    {
        if (_offline != null) return _offline;

        _offline = new MbglOfflineManager();
        _offline.RegionProgress += p => MainThread.BeginInvokeOnMainThread(() =>
            StatusMessage = p.Complete
                ? $"Offline map area ready — {p.CompletedResources} tiles, {p.CompletedBytes / 1024} KB cached"
                : $"Caching map area… {p.CompletedResources} tiles, {p.CompletedBytes / 1024} KB");
        _offline.RegionError += e => MainThread.BeginInvokeOnMainThread(() =>
            StatusMessage = $"Offline download error: {e.Message}");
        return _offline;
    }

    /// <summary>Downloads the currently visible region (current zoom, plus two levels in)
    /// into the shared cache so it stays available with no network connection.</summary>
    [RelayCommand]
    private async Task SaveOfflineAreaAsync()
    {
        if (_controller is null)
        {
            StatusMessage = "Map not ready — wait for the style to load";
            return;
        }

        try
        {
            var (latSw, lonSw, latNe, lonNe) = _controller.GetVisibleBounds();
            if (double.IsNaN(latSw))
            {
                StatusMessage = "Map not ready — pan/zoom the map, then try again";
                return;
            }

            double minZoom = Math.Max(0, Math.Floor(_controller.GetZoom()));
            double maxZoom = Math.Min(minZoom + 2, 16);

            var mgr = GetOfflineManager();
            StatusMessage = $"Caching map area (z{minZoom:0}–{maxZoom:0})…";

            var region = await mgr.CreateRegionAsync(
                StyleUrl, latSw, lonSw, latNe, lonNe, minZoom, maxZoom,
                includeIdeographs: false);

            mgr.ObserveRegion(region.Id);
            mgr.SetDownloadState(region.Id, active: true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Offline download failed: {ex.Message}";
        }
    }

    /// <summary>Forces MapLibre offline (serve only cached tiles) or back online.</summary>
    [RelayCommand]
    private void ToggleOffline()
    {
        IsOffline = !IsOffline;
        MbglNetwork.Online = !IsOffline;
        StatusMessage = IsOffline
            ? "Offline mode — showing cached map tiles only"
            : "Online mode — map tiles load from the network";
    }

    // ── Live AP source ────────────────────────────────────────────────────────

    [RelayCommand]
    private Task LoadMappableApsAsync()
    {
        // Plot the current scan's APs (live IsActive + coordinates), not a DB reload — so the
        // map shows active vs dead exactly as the Scan list does, and updates as scanning runs.
        MappableAps  = _scan.AllKnownAps.Where(a => a.Latitude.HasValue && a.Longitude.HasValue).ToList();
        ApsGeoJson   = BuildGeoJson(MappableAps);
        StatusMessage = $"{MappableAps.Count} APs with GPS";
        return Task.CompletedTask;
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
                tileUrl:          $"{WifiDbSettings.ApiBaseUrl}/tilejson.php?bucket={bucket}",
                tileUrlTemplates: null,
                minZoom: 0, maxZoom: 22);

            var style = _bucketStyles.TryGetValue(bucket, out var s) ? s : new BucketStyle(layer.ActiveColor, layer.ActiveColor, layer.ActiveColor, 2.5);
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
                tileUrl:    $"{WifiDbSettings.ApiBaseUrl}/tilejson.php?bucket={bucket}",
                tileUrlTemplates: null, minZoom: 0, maxZoom: 22);

            var style = _bucketStyles.TryGetValue(bucket, out var s) ? s : CellBucketStyles["cell_monthly"];
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
                    if (string.IsNullOrWhiteSpace(info.Ssid))           info.Ssid           = local.Ssid;
                    if (string.IsNullOrWhiteSpace(info.Authentication)) info.Authentication  = local.AuthText;
                    if (string.IsNullOrWhiteSpace(info.Encryption))     info.Encryption      = local.EncryptionText;
                    if (string.IsNullOrWhiteSpace(info.RadioType))      info.RadioType       = local.RadioType;
                    if (string.IsNullOrWhiteSpace(info.Rssi) && local.Rssi.HasValue) info.Rssi = $"{local.Rssi.Value} dBm";
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
            var auth   = ap.AuthText.Replace("\"", "\\\"");
            var encry  = ap.EncryptionText.Replace("\"", "\\\"");
            var radio  = (ap.RadioType ?? "").Replace("\"", "\\\"");
            var manuf  = ap.Manufacturer?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
            var active = ap.IsActive ? "true" : "false";
            // sectype 1=open, 2=WEP, 3=secure; styidx folds active/dead + sectype into a
            // single 1..6 value the live layer colors from (active 1/2/3, dead 4/5/6).
            int sectype = ap.Authentication == AuthenticationType.Open
                ? 1 : (ap.Encryption == EncryptionType.WEP ? 2 : 3);
            int styidx  = ap.IsActive ? sectype : sectype + 3;
            return $"{{\"type\":\"Feature\","
                 + $"\"geometry\":{{\"type\":\"Point\",\"coordinates\":[{ap.Longitude:F6},{ap.Latitude:F6}]}},"
                 + $"\"properties\":{{\"ssid\":\"{ssid}\",\"bssid\":\"{bssid}\","
                 + $"\"auth\":\"{auth}\",\"encry\":\"{encry}\",\"radio\":\"{radio}\","
                 + $"\"manuf\":\"{manuf}\",\"rssi\":{ap.Rssi ?? 0},"
                 + $"\"signal\":{ap.Signal ?? 0},\"channel\":{ap.Channel},\"isActive\":{active},"
                 + $"\"sectype\":{sectype},\"styidx\":{styidx}}}}}";
        });
        return $"{{\"type\":\"FeatureCollection\",\"features\":[{string.Join(",", features)}]}}";
    }
}

