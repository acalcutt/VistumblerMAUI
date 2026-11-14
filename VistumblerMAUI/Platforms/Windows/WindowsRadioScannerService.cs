#if WINDOWS
using System.Globalization;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using VistumblerMAUI.Services;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

namespace VistumblerMAUI.Platforms.Windows;

/// <summary>
/// Bluetooth devices and cell towers on Windows, as VistumblerCS collects them (its WindowsRadioScannerService), with
/// the records made as the Android scanner makes them so the two match in VS1, WiGLE CSV and WifiDB:
/// <list type="bullet">
/// <item>Bluetooth LE: a <see cref="BluetoothLEAdvertisementWatcher"/>, scanning actively.</item>
/// <item>Bluetooth classic: Windows' inquiry, a <see cref="DeviceWatcher"/> over Bluetooth endpoints started again
/// each time a round of it ends.</item>
/// <item>Cells: the serving and neighbouring cells of the mobile broadband modems (WindowsRadioScannerService.Cells.cs),
/// polled every 5 s. Most PCs have none.</item>
/// </list>
/// What's heard is reported every 2 s, each device once a batch with its strongest reading.
/// </summary>
public sealed partial class WindowsRadioScannerService : IRadioScannerService, IDisposable
{
    public event EventHandler<IReadOnlyList<RadioNetwork>>? NetworksDetected;

    // Windows' protocol id for classic Bluetooth endpoints (the AEP selector's ProtocolId)
    private const string ClassicProtocolSelector = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";
    private const string SignalProperty = "System.Devices.Aep.SignalStrength";
    private const string PairedProperty = "System.Devices.Aep.IsPaired";
    private const string MajorClassProperty = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const string MinorClassProperty = "System.Devices.Aep.Bluetooth.Cod.Minor";
    private static readonly string[] ClassicProperties = [AddressProperty, SignalProperty, PairedProperty, MajorClassProperty, MinorClassProperty];

    // Looked for once, in the background from startup; the Scan page and Settings ask for the answer as they open
    private static readonly TimeSpan DetectWait = TimeSpan.FromSeconds(3);
    private readonly Task<bool> _hasBluetooth = Task.Run(DetectBluetoothAsync);

    private readonly object _lock = new();
    private readonly Dictionary<string, RadioNetwork> _pending = new(StringComparer.OrdinalIgnoreCase);
    // A BLE device's name comes in some advertisements and not others: the last one seen is kept
    private readonly Dictionary<string, string> _leNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceInformation> _classicDevices = new();
    private BluetoothLEAdvertisementWatcher? _leWatcher;
    private DeviceWatcher? _classicWatcher;
    private Timer? _flushTimer;
    private volatile bool _running;
    private int _generation;   // which Start a late cell start (StartCellsAsync) belongs to

    public bool SupportsBluetooth => _hasBluetooth.Wait(DetectWait) && _hasBluetooth.Result;
    public bool IsRunning => _running;

    // Nothing to ask for: a desktop app may use Bluetooth and the modems without a prompt
    public Task<bool> RequestPermissionsAsync(bool cells, bool bluetooth) => Task.FromResult(true);

    public void Start(bool cells, bool bluetooth)
    {
        Stop();
        _running = true;
        int generation = Interlocked.Increment(ref _generation);
        if (bluetooth && SupportsBluetooth)
        {
            StartLowEnergy();
            StartClassic();
        }
        if (cells) _ = StartCellsAsync(generation);
        _flushTimer = new Timer(_ => Flush(), null, 2000, 2000);
    }

    public void Stop()
    {
        _running = false;
        Interlocked.Increment(ref _generation);
        _flushTimer?.Dispose();
        _flushTimer = null;
        if (_leWatcher is { } le)
        {
            _leWatcher = null;
            le.Received -= OnAdvertisement;
            try { le.Stop(); } catch { /* already stopped */ }
        }
        if (_classicWatcher is { } classic)
        {
            _classicWatcher = null;
            try
            {
                if (classic.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) classic.Stop();
            }
            catch { /* already stopped */ }
        }
        StopCells();
        lock (_lock)
        {
            _pending.Clear();
            _classicDevices.Clear();
        }
    }

    public void Dispose() => Stop();

    private static async Task<bool> DetectBluetoothAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            return adapter is not null && (adapter.IsLowEnergySupported || adapter.IsClassicSupported);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Radio] no Bluetooth: {ex.Message}");
            return false;
        }
    }

    /// <summary>Queues what was heard for the next batch, merged with what's already queued for it.</summary>
    private void Heard(RadioNetwork n)
    {
        lock (_lock)
        {
            if (_pending.TryGetValue(n.Key, out var seen))
            {
                if (n.Rssi > seen.Rssi) seen.Rssi = n.Rssi;
                if (n.Type == RadioNetworkType.BLE && seen.Type == RadioNetworkType.BT) seen.Type = n.Type;   // LE wins, as in WiGLE
                if (!string.IsNullOrEmpty(n.Name)) seen.Name = n.Name;
                // A classic report's device class beats an LE one's "Misc"
                if (n.Capabilities.Length > 0 && (seen.Capabilities.Length == 0 || seen.Capabilities.StartsWith("Misc", StringComparison.Ordinal)))
                    seen.Capabilities = n.Capabilities;
                if (n.Frequency != 0) seen.Frequency = n.Frequency;
                seen.Channel ??= n.Channel;
                seen.MfgrId ??= n.MfgrId;
                return;
            }
            var now = DateTime.UtcNow;
            n.FirstSeen = n.LastSeen = now;
            n.IsActive = true;
            _pending[n.Key] = n;
        }
    }

    private void Flush()
    {
        List<RadioNetwork> batch;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            batch = _pending.Values.ToList();
            _pending.Clear();
        }
        NetworksDetected?.Invoke(this, batch);
    }

    // ── Bluetooth LE ──────────────────────────────────────────────────────────

    private void StartLowEnergy()
    {
        try
        {
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            watcher.Received += OnAdvertisement;
            watcher.Stopped += (_, e) =>
            {
                if (e.Error != BluetoothError.Success) DebugLog.Write($"[Radio] Bluetooth LE scan stopped: {e.Error}");
            };
            _leWatcher = watcher;
            watcher.Start();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Radio] Bluetooth LE scan failed: {ex.Message}");   // no LE radio, or it's switched off
            _leWatcher = null;
        }
    }

    private void OnAdvertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        string address = FormatAddress(args.BluetoothAddress);
        string name = args.Advertisement.LocalName ?? string.Empty;
        lock (_lock)
        {
            if (name.Length > 0) _leNames[address] = name;
            else _leNames.TryGetValue(address, out name!);
        }
        var maker = args.Advertisement.ManufacturerData.LastOrDefault();   // as Android takes it: the last one
        Heard(new RadioNetwork
        {
            Key = address,
            Type = RadioNetworkType.BLE,
            Name = name ?? string.Empty,
            // An advertisement carries no device class: class 0, as Android records an LE device without one
            Capabilities = BluetoothClassNames.For(0),
            MfgrId = maker?.CompanyId,
            Rssi = args.RawSignalStrengthInDBm,
        });
    }

    // ── Bluetooth classic ─────────────────────────────────────────────────────

    private void StartClassic()
    {
        try
        {
            var watcher = DeviceInformation.CreateWatcher(ClassicProtocolSelector, ClassicProperties, DeviceInformationKind.AssociationEndpoint);
            watcher.Added += (_, info) =>
            {
                lock (_lock) _classicDevices[info.Id] = info;
                ClassicHeard(info);
            };
            watcher.Updated += (_, update) =>
            {
                DeviceInformation? info;
                lock (_lock)
                {
                    if (!_classicDevices.TryGetValue(update.Id, out info)) return;
                    info.Update(update);
                }
                ClassicHeard(info);
            };
            // A round of inquiry ends; another starts while scanning goes on, as Windows' own Bluetooth page does
            watcher.EnumerationCompleted += (w, _) => { try { w.Stop(); } catch { } };
            watcher.Stopped += (w, _) =>
            {
                if (_running && ReferenceEquals(w, _classicWatcher))
                    try { w.Start(); } catch (Exception ex) { DebugLog.Write($"[Radio] Bluetooth inquiry restart failed: {ex.Message}"); }
            };
            _classicWatcher = watcher;
            watcher.Start();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Radio] Bluetooth inquiry failed: {ex.Message}");
            _classicWatcher = null;
        }
    }

    private void ClassicHeard(DeviceInformation info)
    {
        // Paired devices are listed whether they're in range or not: only one with a signal was heard
        if (Number(info, SignalProperty) is not { } rssi) return;
        if (!info.Properties.TryGetValue(AddressProperty, out var a) || a is not string address || address.Length == 0) return;
        int deviceClass = BluetoothClassNames.Code(Number(info, MajorClassProperty) ?? 0, Number(info, MinorClassProperty) ?? 0);
        bool paired = info.Properties.TryGetValue(PairedProperty, out var p) && p is true;

        Heard(new RadioNetwork
        {
            Key = address.ToUpperInvariant().Replace('-', ':'),
            Type = RadioNetworkType.BT,
            Name = info.Name ?? string.Empty,
            // As Android and WiGLE write it: the device class, then ';' and the bond state (10 none, 12 bonded)
            Capabilities = $"{BluetoothClassNames.For(deviceClass)};{(paired ? 12 : 10)}",
            Frequency = deviceClass,
            Rssi = rssi,
        });
    }

    // A device property's number, whichever integer type Windows gives it as
    private static int? Number(DeviceInformation info, string property)
    {
        if (!info.Properties.TryGetValue(property, out var value) || value is not IConvertible number) return null;
        try { return number.ToInt32(CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    internal static string FormatAddress(ulong address)
        => string.Join(':', Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2")));
}
#endif
