using VistumblerMAUI.Localization;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using VistumblerMAUI.Controls;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// The original Vistumbler's GPS Details and GPS Compass windows in one page (menu → GPS details): the live fix
/// from whichever GPS source is in use, and a compass pointing along the direction of travel.
/// </summary>
public partial class GpsDetailsViewModel : ObservableObject
{
    private readonly IGpsService _gps;
    private readonly ScanViewModel _scan;
    private IDispatcherTimer? _timer;

    public CompassDrawable Compass { get; } = new();
    public event Action? CompassUpdated;

    [ObservableProperty] private string _source = string.Empty;
    [ObservableProperty] private string _status = Loc.T("GpsDetails_Waiting");
    [ObservableProperty] private string _position = "—";
    [ObservableProperty] private string _altitude = "—";
    [ObservableProperty] private string _satellites = "—";
    [ObservableProperty] private string _accuracy = "—";
    [ObservableProperty] private string _speed = "—";
    [ObservableProperty] private string _heading = "—";
    [ObservableProperty] private string _quality = "—";
    [ObservableProperty] private string _fixTime = "—";

    public GpsDetailsViewModel(IGpsService gps, ScanViewModel scan)
    {
        _gps = gps;
        _scan = scan;
    }

    /// <summary>Starts following the GPS while the page is open.</summary>
    public void Start()
    {
        Source = Loc.Opt(GpsSettings.AvailableSources.FirstOrDefault(s => s.Source == GpsSettings.Source).Name ?? "");
        _gps.GpsDataReceived += OnGpsData;
        if (_gps.CurrentGpsData is { } current) Show(current);
        // Ages the "seconds since the last fix" line between fixes
        _timer ??= Application.Current?.Dispatcher.CreateTimer();
        if (_timer is not null)
        {
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Start();
        }
        OnTick(null, EventArgs.Empty);
        StartWifiPosition();
    }

    public void Stop()
    {
        _gps.GpsDataReceived -= OnGpsData;
        _timer?.Stop();
    }

    private void OnGpsData(object? sender, GpsDataReceivedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => Show(e.GpsData));

    private void OnTick(object? sender, EventArgs e)
    {
        WifiPositionTick();
        if (!_gps.IsActive && _gps.CurrentGpsData is null)
        {
            Status = Loc.T("GpsDetails_Off");
            return;
        }
        double age = _gps.SecondsSinceLastUpdate;
        Status = age == double.MaxValue ? Loc.T("GpsDetails_Waiting") : Loc.T("GpsDetails_LastFix", age.ToString("0"));
    }

    private void Show(GpsData d)
    {
        var inv = CultureInfo.InvariantCulture;
        Position   = GpsFormatter.ToText(d.Latitude, d.Longitude);
        Altitude   = d.Altitude is { } alt ? $"{alt.ToString("0.0", inv)} m ({(alt * 3.28084).ToString("0", inv)} ft)" : "—";
        Satellites = d.NumberOfSatellites > 0 ? d.NumberOfSatellites.ToString(inv) : "—";
        Accuracy   = d.Accuracy is { } acc ? $"± {acc.ToString("0", inv)} m"
                   : d.HorizontalDilution is { } hdop ? $"HDOP {hdop.ToString("0.0", inv)}" : "—";
        Speed      = d.SpeedKnots is { } kn
            ? $"{d.SpeedKmh.ToString("0.0", inv)} km/h · {d.SpeedMph.ToString("0.0", inv)} mph · {kn.ToString("0.0", inv)} kn"
            : "—";
        // A heading means little standing still (receivers report noise or 0), so show it only when moving
        bool moving = d.SpeedKnots is > 0.5;
        Heading    = d.TrackAngle is { } t && moving ? $"{t.ToString("0", inv)}° {Cardinal(t)}" : "—";
        Quality    = d.Quality.ToString();
        FixTime    = d.Timestamp == default ? "—" : d.Timestamp.ToLocalTime().ToString("HH:mm:ss", inv);
        Compass.Heading = moving ? d.TrackAngle : null;
        CompassUpdated?.Invoke();
    }

    private static string Cardinal(double deg) =>
        new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[(int)Math.Round(((deg % 360) + 360) % 360 / 45) % 8];
}
