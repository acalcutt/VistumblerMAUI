using System.Diagnostics;
using MapLibreNative.Maui.Handlers;
using MapLibreNative.Maui.Handlers.EventArgs;
using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class MapPage : ContentPage
{
    private const string LogTag = "[MapPage]";

    private readonly MapViewModel _vm;
    private bool _mapReadyFired;
    private bool _styleLoadedFired;
    private bool _didBecomeIdleFired;
    private bool _firstAppearLogged;

    public MapPage(MapViewModel vm)
    {
        Log("ctor start");
        InitializeComponent();
        BindingContext = _vm = vm;
        Log($"ctor: BindingContext set, StyleUrl='{_vm.StyleUrl}'");

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
        }
        catch (Exception ex)
        {
            Log($"Map.StyleLoaded handler threw: {ex}");
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

        // Refresh AP count/GeoJSON if we already have data (style already loaded).
        if (_vm.MappableAps.Count > 0)
            _ = _vm.LoadMappableApsCommand.ExecuteAsync(null);
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
