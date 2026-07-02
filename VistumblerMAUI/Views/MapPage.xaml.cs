using System.Diagnostics;
using MapLibreNative.Maui.Handlers;
using MapLibreNative.Maui.Handlers.EventArgs;
using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class MapPage : ContentPage
{
    private const string LogTag = "[MapPage]";

    private readonly MapViewModel _vm;
    private IDispatcherTimer? _liveTimer;   // periodic reload while the map is visible
    private bool _mapReadyFired;
    private bool _styleLoadedFired;
    private bool _didBecomeIdleFired;
    private bool _firstAppearLogged;

    public MapPage(MapViewModel vm, ScanViewModel scan)
    {
        Log("ctor start");
        InitializeComponent();
        BindingContext = _vm = vm;
        ScanBar.BindingContext = scan;   // shared control bar reflects the live scan state
        Log($"ctor: BindingContext set, StyleUrl='{_vm.StyleUrl}'");

        // Redraw the live AP layer whenever its GeoJSON changes (scan/timer refresh).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MapViewModel.ApsGeoJson))
                MainThread.BeginInvokeOnMainThread(RefreshLiveApLayer);
        };

        // Log handler lifecycle on the MapLibreMap view itself.
        Map.HandlerChanging += (_, e) =>
            Log($"Map.HandlerChanging old={Describe(e.OldHandler)} new={Describe(e.NewHandler)}");
        Map.HandlerChanged += (_, _) =>
            Log($"Map.HandlerChanged handler={Describe(Map.Handler)} " +
                $"PlatformView={(Map.Handler?.PlatformView is null ? "null" : Map.Handler.PlatformView.GetType().FullName)}");

        Map.Loaded   += (_, _) => Log($"Map.Loaded  Width={Map.Width} Height={Map.Height} IsVisible={Map.IsVisible}");
        Map.Unloaded += (_, _) => Log("Map.Unloaded");
        Map.SizeChanged += (_, _) => Log($"Map.SizeChanged W={Map.Width} H={Map.Height}");

        Map.MapReady += OnMapReady;
        Map.StyleLoaded += OnStyleLoaded;
        Map.MapClick += OnMapClick;
        Map.DidBecomeIdle += (_, _) =>
        {
            if (!_didBecomeIdleFired)
            {
                _didBecomeIdleFired = true;
                Log("Map.DidBecomeIdle (first)");
            }
        };
        Map.CameraIdle += (_, _) => Log($"Map.CameraIdle zoom={SafeGetZoom()}");
    }

    private void OnMapReady(object? sender, EventArgs e)
    {
        _mapReadyFired = true;
        var ctrl = (Map.Handler as MapLibreMapHandler)?.Controller;
        Log($"Map.MapReady controller={(ctrl is null ? "null" : "set")} reapplying StyleUrl='{_vm.StyleUrl}'");

        // Hook controller-only diagnostic events (not exposed on MapLibreMap virtual view).
        if (ctrl is not null)
        {
            ctrl.OnDidFailLoadingMapReceived += msg =>
                Log($"!! OnDidFailLoadingMap: {msg}");
            ctrl.OnStyleImageMissingReceived += img =>
                Log($"OnStyleImageMissing: {img}");
        }

        // Workaround: On Windows the property mapper fires UpdateStyleUrl before the
        // native MbglMap is created, so SetStyleString returns early and no style is
        // loaded. Re-applying here triggers the actual load.
        try
        {
            ctrl?.SetStyleString(_vm.StyleUrl);
        }
        catch (Exception ex)
        {
            Log($"Map.MapReady SetStyleString threw: {ex}");
        }
    }

    private async void OnMapClick(object? sender, MapClickEventArgs e)
    {
        try
        {
            await _vm.OnMapTappedAsync(e.ScreenX, e.ScreenY);
        }
        catch (Exception ex)
        {
            Log($"OnMapClick threw: {ex}");
        }
    }

    private void OnStyleLoaded(object? sender, EventArgs e)
    {
        _styleLoadedFired = true;
        var ctrl = (Map.Handler as MapLibreMapHandler)?.Controller;
        Log($"Map.StyleLoaded controller={(ctrl is null ? "null" : "set")}");

        try
        {
            if (ctrl is not null)
                _vm.OnMapControllerReady(ctrl);
            else
                Log("Map.StyleLoaded: controller was null — skipping OnMapControllerReady");

            _ = _vm.LoadMappableApsCommand.ExecuteAsync(null);
            RefreshLiveApLayer();   // (re)create the live AP source+layer on the fresh style
        }
        catch (Exception ex)
        {
            Log($"Map.StyleLoaded handler threw: {ex}");
        }
    }

    /// <summary>
    /// (Re)create the live-scan AP layer imperatively from the current GeoJSON. The declarative
    /// GeoJsonSource only accepts a FeatureCollection object (not our JSON string) and doesn't
    /// update after creation, so the live layer is driven from code — replacing the source data
    /// and re-adding the circle layer on top (above any history overlays).
    /// </summary>
    private void RefreshLiveApLayer()
    {
        var ctrl = (Map.Handler as MapLibreMapHandler)?.Controller;
        if (ctrl is null || !_styleLoadedFired) return;
        try
        {
            ctrl.RemoveLayer("ap-circles");
            ctrl.SetGeoJsonFeature("ap-source", _vm.ApsGeoJson);   // replace source data
            ctrl.AddCircleLayer("ap-circles", "ap-source", belowLayerId: null,
                sourceLayer: null, properties: _vm.ApLayerProperties);
        }
        catch (Exception ex)
        {
            Log($"RefreshLiveApLayer threw: {ex}");
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Log($"OnAppearing handler={Describe(Map.Handler)} mapReady={_mapReadyFired} styleLoaded={_styleLoadedFired} " +
            $"Map.W={Map.Width} Map.H={Map.Height} Map.IsVisible={Map.IsVisible}");

        if (!_firstAppearLogged)
        {
            _firstAppearLogged = true;
            // Watchdog: flag if events never fire after a reasonable delay.
            _ = StartWatchdogAsync();
        }

        // Pick up a basemap style changed in Settings. Assigning StyleUrl reloads the
        // map style, and OnMapControllerReady (fired on StyleLoaded) re-applies the
        // active history layers.
        bool styleChanged = _vm.StyleUrl != Services.MapStyles.StyleUrl;
        if (styleChanged)
            _vm.StyleUrl = Services.MapStyles.StyleUrl;

        // Pick up AP colors changed in Settings. Rebuild the live-layer paint + bucket
        // styles, then force a style reload (unless the basemap change above already
        // triggered one) so the history layers and declarative live layer re-apply with
        // the new colors — circle paint is fixed when a layer is committed to the style.
        if (_vm.AppliedColorRevision != Services.MapColors.Revision)
        {
            _vm.RefreshMapColors();
            if (!styleChanged)
            {
                try { (Map.Handler as MapLibreMapHandler)?.Controller?.SetStyleString(_vm.StyleUrl); }
                catch (Exception ex) { Log($"OnAppearing color reload SetStyleString threw: {ex}"); }
            }
        }

        // Refresh AP count/GeoJSON if we already have data (style already loaded).
        if (_vm.MappableAps.Count > 0)
            _ = _vm.LoadMappableApsCommand.ExecuteAsync(null);

        // Live-refresh the plotted APs while the map is on screen, so APs scanned (with a
        // GPS fix) show up without leaving the page. Reloads from the DB the scan writes to.
        _liveTimer ??= CreateLiveTimer();
        _liveTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _liveTimer?.Stop();
    }

    private IDispatcherTimer CreateLiveTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(3);
        timer.Tick += (_, _) =>
        {
            // CanExecute is false while a load is still running (AsyncRelayCommand),
            // so ticks never overlap.
            if (_vm.LoadMappableApsCommand.CanExecute(null))
                _ = _vm.LoadMappableApsCommand.ExecuteAsync(null);
        };
        return timer;
    }

    private async Task StartWatchdogAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        Log($"WATCHDOG t=5s mapReady={_mapReadyFired} styleLoaded={_styleLoadedFired} " +
            $"didBecomeIdle={_didBecomeIdleFired} handler={Describe(Map.Handler)} " +
            $"W={Map.Width} H={Map.Height}");

        await Task.Delay(TimeSpan.FromSeconds(10));
        Log($"WATCHDOG t=15s mapReady={_mapReadyFired} styleLoaded={_styleLoadedFired} " +
            $"didBecomeIdle={_didBecomeIdleFired}");

        if (!_mapReadyFired)
            Log("WATCHDOG: MapReady never fired — handler/CreatePlatformView likely failed " +
                "(check that MauiProgram registered MapLibreMapHandler and that Window service is resolvable).");
        else if (!_styleLoadedFired)
            Log("WATCHDOG: MapReady fired but StyleLoaded did not — style URL load failed " +
                $"(URL='{_vm.StyleUrl}'). Check network access and tile server reachability.");
        else if (!_didBecomeIdleFired)
            Log("WATCHDOG: Style loaded but never reached idle — render loop may be stalled.");
    }

    private double SafeGetZoom()
    {
        try { return (Map.Handler as MapLibreMapHandler)?.Controller?.GetZoom() ?? double.NaN; }
        catch { return double.NaN; }
    }

    private static string Describe(object? handler)
        => handler is null ? "null" : handler.GetType().Name;

    private static void Log(string msg)
        => Debug.WriteLine($"{LogTag} {DateTime.Now:HH:mm:ss.fff} {msg}");
}
