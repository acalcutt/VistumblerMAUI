#if WINDOWS
using System.Globalization;
using System.IO.Ports;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.Platforms.Windows;

/// <summary>
/// GPS source that reads NMEA 0183 sentences (GGA/RMC) from a serial COM port — the desktop
/// receiver option from VistumblerCS. Port + baud come from <see cref="GpsSettings"/>. Windows
/// only (serial ports aren't available on Android/iOS).
/// </summary>
public class SerialNmeaGpsService : ISerialGpsService
{
    private SerialPort? _port;
    private bool _connected;
    private GpsData? _current;
    private DateTime _lastUpdate = DateTime.MinValue;
    private CancellationTokenSource? _cts;

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>?        GpsError;

    public GpsData? CurrentGpsData => _current;
    public bool     IsActive       => _connected;
    public double   SecondsSinceLastUpdate =>
        _lastUpdate == DateTime.MinValue ? double.MaxValue : (DateTime.UtcNow - _lastUpdate).TotalSeconds;

    public string[] GetAvailablePorts()
    {
        try { return SerialPort.GetPortNames(); } catch { return Array.Empty<string>(); }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_connected) return Task.CompletedTask;

        var portName = GpsSettings.ComPort;
        if (string.IsNullOrWhiteSpace(portName))
        {
            GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = "no COM port selected" });
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            _port = new SerialPort
            {
                PortName     = portName,
                BaudRate     = GpsSettings.BaudRate,
                Parity       = Parity.None,
                DataBits     = 8,
                StopBits     = StopBits.One,
                ReadTimeout  = 1000,
                WriteTimeout = 1000,
                NewLine      = "\n"
            };
            _port.DataReceived += OnDataReceived;
            _port.Open();
            _connected = true;
        }
        catch (Exception ex)
        {
            GpsError?.Invoke(this, new GpsErrorEventArgs
            {
                ErrorMessage = $"could not open {portName} ({ex.Message})",
                Exception    = ex
            });
        }
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _connected = false;
        if (_port is not null)
        {
            _port.DataReceived -= OnDataReceived;
            try { if (_port.IsOpen) _port.Close(); } catch { }
            _port.Dispose();
            _port = null;
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (_port is null || !_port.IsOpen) return;
            ProcessSentence(_port.ReadLine().Trim());
        }
        catch (TimeoutException) { /* ignore */ }
        catch (Exception ex)
        {
            GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = "read error", Exception = ex });
        }
    }

    private void ProcessSentence(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence) || !sentence.StartsWith("$")) return;
        var p = sentence.Split(',');
        if (p.Length < 1) return;

        try
        {
            if (p[0] is "$GPGGA" or "$GNGGA") ParseGga(p);
            else if (p[0] is "$GPRMC" or "$GNRMC") ParseRmc(p);
        }
        catch { /* skip malformed sentence */ }
    }

    private void ParseGga(string[] p)
    {
        // $..GGA,time,lat,N/S,lon,E/W,quality,sats,hdop,alt,M,...
        if (p.Length < 15) return;
        _current ??= new GpsData();

        if (!string.IsNullOrEmpty(p[2]) && !string.IsNullOrEmpty(p[3]))
            _current.Latitude = ToDecimalDegrees(p[2], p[3]);
        if (!string.IsNullOrEmpty(p[4]) && !string.IsNullOrEmpty(p[5]))
            _current.Longitude = ToDecimalDegrees(p[4], p[5]);
        if (int.TryParse(p[6], out int q)) _current.Quality = (GpsQuality)q;
        if (int.TryParse(p[7], out int sats)) _current.NumberOfSatellites = sats;
        if (double.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out double hdop)) _current.HorizontalDilution = hdop;
        if (double.TryParse(p[9], NumberStyles.Float, CultureInfo.InvariantCulture, out double alt)) _current.Altitude = alt;

        Publish();
    }

    private void ParseRmc(string[] p)
    {
        // $..RMC,time,status,lat,N/S,lon,E/W,speed,track,date,...
        if (p.Length < 12) return;
        if (p[2] != "A") return;               // A = valid fix
        _current ??= new GpsData();

        if (!string.IsNullOrEmpty(p[3]) && !string.IsNullOrEmpty(p[4]))
            _current.Latitude = ToDecimalDegrees(p[3], p[4]);
        if (!string.IsNullOrEmpty(p[5]) && !string.IsNullOrEmpty(p[6]))
            _current.Longitude = ToDecimalDegrees(p[5], p[6]);
        if (double.TryParse(p[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double kn)) _current.SpeedKnots = kn;
        if (double.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out double trk)) _current.TrackAngle = trk;
        if (_current.Quality == GpsQuality.Invalid) _current.Quality = GpsQuality.GpsFix;

        Publish();
    }

    // ddmm.mmmm / dddmm.mmmm + hemisphere → signed decimal degrees.
    private static double ToDecimalDegrees(string coordinate, string direction)
    {
        int dot = coordinate.IndexOf('.');
        if (dot < 3) return 0;
        int degLen = dot - 2;
        if (!double.TryParse(coordinate[..degLen], NumberStyles.Float, CultureInfo.InvariantCulture, out double deg) ||
            !double.TryParse(coordinate[degLen..], NumberStyles.Float, CultureInfo.InvariantCulture, out double min))
            return 0;

        double dd = deg + min / 60.0;
        if (direction is "S" or "W") dd = -dd;
        return dd;
    }

    private void Publish()
    {
        if (_current is null) return;
        if (_current.Quality == GpsQuality.Invalid) return;  // don't emit until there's a fix
        _lastUpdate = DateTime.UtcNow;
        _current.Timestamp = DateTime.UtcNow;
        GpsDataReceived?.Invoke(this, new GpsDataReceivedEventArgs { GpsData = _current });
    }
}
#endif
