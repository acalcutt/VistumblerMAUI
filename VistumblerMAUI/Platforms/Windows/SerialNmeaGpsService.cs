#if WINDOWS
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
    private readonly NmeaParser _parser = new();
    private DateTime _lastUpdate = DateTime.MinValue;
    private CancellationTokenSource? _cts;
    private Timer? _watchdog;
    private DateTime _lastData = DateTime.UtcNow;
    private readonly object _reopenLock = new();

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>?        GpsError;

    public GpsData? CurrentGpsData => _parser.Current;
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

        if (string.IsNullOrWhiteSpace(GpsSettings.ComPort))
        {
            Error("no COM port selected");
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connected = OpenPort();

        // The original's "Disconnect GPS when no data is received in over 10 seconds", except that the port is
        // reopened rather than GPS turned off; a port that failed to open is retried the same way
        _lastData = DateTime.UtcNow;
        _watchdog = new Timer(_ => CheckPort(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _watchdog?.Dispose();
        _watchdog = null;
        _connected = false;
        ClosePort();
    }

    private bool OpenPort()
    {
        var portName = GpsSettings.ComPort;
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
            return true;
        }
        catch (Exception ex)
        {
            ClosePort();
            Error($"could not open {portName} ({ex.Message})", ex);
            return false;
        }
    }

    private void ClosePort()
    {
        var port = _port;
        _port = null;
        if (port is null) return;
        port.DataReceived -= OnDataReceived;
        try { if (port.IsOpen) port.Close(); } catch { }
        port.Dispose();
    }

    private void CheckPort()
    {
        if (_cts is null || _cts.IsCancellationRequested) return;
        // Acts every NoDataTimeout at most: on a silent port when the option is on, and on a port that didn't open
        if (DateTime.UtcNow - _lastData <= GpsSettings.NoDataTimeout) return;
        if (_port is not null && !GpsSettings.ReconnectWhenNoData) return;
        if (!Monitor.TryEnter(_reopenLock)) return;
        try
        {
            if (_port is not null)
                Error($"no data from {GpsSettings.ComPort} for {GpsSettings.NoDataTimeout.TotalSeconds:0} seconds; reopening it");
            ClosePort();
            _lastData = DateTime.UtcNow;   // give the reopened port a full timeout
            _connected = OpenPort();
        }
        finally
        {
            Monitor.Exit(_reopenLock);
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            // Read every complete line that has arrived; one per event would let the buffer fall behind
            while (_port is { IsOpen: true } port && port.BytesToRead > 0)
            {
                _lastData = DateTime.UtcNow;
                ProcessSentence(port.ReadLine().Trim());
            }
        }
        catch (TimeoutException) { /* the rest of a line hasn't arrived yet; it stays buffered */ }
        catch (Exception ex)
        {
            Error("read error", ex);
        }
    }

    private void ProcessSentence(string sentence)
    {
        if (_parser.Process(sentence) is not { } fix) return;
        _lastUpdate = DateTime.UtcNow;
        GpsDataReceived?.Invoke(this, new GpsDataReceivedEventArgs { GpsData = fix });
    }

    private void Error(string message, Exception? ex = null) =>
        GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = message, Exception = ex });
}
#endif
