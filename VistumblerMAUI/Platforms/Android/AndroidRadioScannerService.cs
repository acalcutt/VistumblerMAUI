#if ANDROID
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Android.Telephony;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using AndroidApp = Android.App.Application;

namespace VistumblerMAUI.Platforms.Android;

/// <summary>
/// Cell towers and Bluetooth devices on Android, collected as WiGLE WiFi Wardriving collects them (CellReceiver,
/// GsmOperator and BluetoothReceiver, BSD 3-clause; see THIRD-PARTY-NOTICES.md) so the records match WiGLE's and
/// WifiDB's: cells keyed "mccmnc_lac_cid", Bluetooth by MAC with WiGLE's device class names.
/// <list type="bullet">
/// <item>Cells: TelephonyManager's cell info every 5 s (asking for a fresh update on Android 10+).</item>
/// <item>Bluetooth classic: back-to-back discovery, about 12 s each.</item>
/// <item>Bluetooth LE: one continuous scan. Readings are collected and reported every 2 s.</item>
/// </list>
/// </summary>
public sealed class AndroidRadioScannerService : IRadioScannerService
{
    public event EventHandler<IReadOnlyList<RadioNetwork>>? NetworksDetected;

    public bool SupportsCells => AndroidApp.Context.PackageManager?.HasSystemFeature("android.hardware.telephony") == true;
    public bool SupportsBluetooth => BluetoothAdapter() is not null;
    public bool IsRunning => _cellTimer is not null || _btTimer is not null;

    private Timer? _cellTimer, _btTimer;
    private BtDiscoveryReceiver? _discoveryReceiver;
    private LeCallback? _leCallback;
    private readonly Dictionary<string, RadioNetwork> _btPending = new();
    private readonly object _btLock = new();

    public async Task<bool> RequestPermissionsAsync(bool cells, bool bluetooth)
    {
        try
        {
            if (await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted &&
                await Permissions.RequestAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted)
                return false;
            if (bluetooth &&
                await Permissions.CheckStatusAsync<NearbyDevicesPermission>() != PermissionStatus.Granted &&
                await Permissions.RequestAsync<NearbyDevicesPermission>() != PermissionStatus.Granted)
                return false;
            return true;
        }
        catch (Exception ex)
        {
            Services.DebugLog.Write($"[Radio] permission request failed: {ex.Message}");
            return false;
        }
    }

    // Android 12+ "Nearby devices": scanning, and reading a device's name and class. Older versions grant Bluetooth at
    // install and scan under the location permission.
    private sealed class NearbyDevicesPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            OperatingSystem.IsAndroidVersionAtLeast(31)
                ? new[] { (global::Android.Manifest.Permission.BluetoothScan, true), (global::Android.Manifest.Permission.BluetoothConnect, true) }
                : Array.Empty<(string, bool)>();
    }

    public void Start(bool cells, bool bluetooth)
    {
        Stop();
        if (cells && SupportsCells) _cellTimer = new Timer(_ => PollCells(), null, 0, 5000);
        if (bluetooth && SupportsBluetooth)
        {
            StartBluetooth();
            _btTimer = new Timer(_ => FlushBluetooth(), null, 2000, 2000);
        }
    }

    public void Stop()
    {
        _cellTimer?.Dispose();
        _cellTimer = null;
        _btTimer?.Dispose();
        _btTimer = null;
        StopBluetooth();
        lock (_btLock) _btPending.Clear();
    }

    // ── Cells ─────────────────────────────────────────────────────────────────

    private static TelephonyManager? Telephony() =>
        AndroidApp.Context.GetSystemService(Context.TelephonyService) as TelephonyManager;

    private void PollCells()
    {
        var tm = Telephony();
        if (tm is null) return;
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                tm.RequestCellInfoUpdate(AndroidApp.Context.MainExecutor!, new CellCallback(list => Report(tm, list)));
            else
                Report(tm, tm.AllCellInfo);
        }
        catch (Java.Lang.SecurityException ex) { Services.DebugLog.Write($"[Radio] cell scan not permitted: {ex.Message}"); }
        catch (Exception ex) { Services.DebugLog.Write($"[Radio] cell scan failed: {ex.Message}"); }
    }

    private void Report(TelephonyManager tm, IList<CellInfo>? cells)
    {
        if (cells is null || cells.Count == 0) return;
        var now = DateTime.UtcNow;
        var found = new List<RadioNetwork>();
        foreach (var cell in cells)
        {
            try
            {
                if (ToNetwork(tm, cell) is { } n)
                {
                    n.FirstSeen = n.LastSeen = now;
                    found.Add(n);
                }
            }
            catch (Exception ex) { Services.DebugLog.Write($"[Radio] skipped a cell: {ex.Message}"); }
        }
        if (found.Count > 0) NetworksDetected?.Invoke(this, found);
    }

    private static RadioNetwork? ToNetwork(TelephonyManager tm, CellInfo cell)
    {
        switch (cell)
        {
            case CellInfoLte lte when lte.CellIdentity is { } id:
                return Gsmish(tm, RadioNetworkType.LTE, id.MccString, id.MncString, id.Tac, id.Ci, id.Earfcn,
                    lte.CellSignalStrength?.Dbm, OperatingSystem.IsAndroidVersionAtLeast(28) ? id.OperatorAlphaLong : null);
            case CellInfoGsm gsm when gsm.CellIdentity is { } id:
                return Gsmish(tm, RadioNetworkType.GSM, id.MccString, id.MncString, id.Lac, id.Cid, id.Arfcn,
                    gsm.CellSignalStrength?.Dbm, OperatingSystem.IsAndroidVersionAtLeast(28) ? id.OperatorAlphaLong : null);
            case CellInfoWcdma wcdma when wcdma.CellIdentity is { } id:
                return Gsmish(tm, RadioNetworkType.WCDMA, id.MccString, id.MncString, id.Lac, id.Cid, id.Uarfcn,
                    wcdma.CellSignalStrength?.Dbm, OperatingSystem.IsAndroidVersionAtLeast(28) ? id.OperatorAlphaLong : null);
            case CellInfoCdma cdma when cdma.CellIdentity is { } id:
            {
                if (id.BasestationId == int.MaxValue || id.NetworkId == int.MaxValue || id.SystemId == int.MaxValue) return null;
                var op = tm.NetworkOperator ?? string.Empty;
                return new RadioNetwork
                {
                    Key = $"{id.SystemId}_{id.NetworkId}_{id.BasestationId}",
                    Type = RadioNetworkType.CDMA,
                    Name = tm.NetworkOperatorName ?? string.Empty,
                    Capabilities = "CDMA;" + op,
                    Rssi = Strength(cdma.CellSignalStrength?.Dbm),
                    IsActive = true,
                };
            }
            default:
                if (OperatingSystem.IsAndroidVersionAtLeast(29) && cell is CellInfoNr nr && nr.CellIdentity is CellIdentityNr nid)
                    return Gsmish(tm, RadioNetworkType.NR, nid.MccString, nid.MncString, nid.Tac, nid.Nci, nid.Nrarfcn,
                        nr.CellSignalStrength?.Dbm, nid.OperatorAlphaLong);
                return null;
        }
    }

    // WiGLE's GsmOperator: "mccmnc_lac_cid", dropping cells whose identity the modem didn't fill in
    private static RadioNetwork? Gsmish(TelephonyManager tm, RadioNetworkType type, string? mcc, string? mnc, int lacOrTac,
        long cellId, int fcn, int? dbm, string? operatorName)
    {
        if (!int.TryParse(mcc, out var mccInt) || mccInt is <= 0 or >= 1000) return null;
        if (!int.TryParse(mnc, out var mncInt) || mncInt is <= 0 or >= 1000) return null;
        if (lacOrTac is <= 0 or int.MaxValue) return null;
        long limit = type == RadioNetworkType.NR ? long.MaxValue : int.MaxValue;
        if (cellId <= 0 || cellId >= limit) return null;

        var op = mcc + mnc;
        if (string.IsNullOrEmpty(operatorName) && tm.NetworkOperator == op) operatorName = tm.NetworkOperatorName;
        int? channel = fcn is > 0 and < int.MaxValue ? fcn : null;
        return new RadioNetwork
        {
            Key = $"{op}_{lacOrTac}_{cellId}",
            Type = type,
            Name = operatorName ?? string.Empty,
            Capabilities = $"{type};{op}",
            Channel = channel,
            Frequency = channel ?? 0,
            Rssi = Strength(dbm),
            IsActive = true,
        };
    }

    private const int CellMinStrength = -113;   // as WiGLE reports a cell with no usable strength
    private static int Strength(int? dbm) => dbm is null or int.MaxValue or 0 ? CellMinStrength : dbm.Value;

    private sealed class CellCallback(Action<IList<CellInfo>> onCells) : TelephonyManager.CellInfoCallback
    {
        public override void OnCellInfo(IList<CellInfo> cellInfo) => onCells(cellInfo);
    }

    // ── Bluetooth ─────────────────────────────────────────────────────────────

    private static BluetoothAdapter? BluetoothAdapter() =>
        (AndroidApp.Context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;

    private void StartBluetooth()
    {
        var adapter = BluetoothAdapter();
        if (adapter is null || !adapter.IsEnabled) return;
        try
        {
            _discoveryReceiver = new BtDiscoveryReceiver(this);
            var filter = new IntentFilter(BluetoothDevice.ActionFound);
            filter.AddAction(global::Android.Bluetooth.BluetoothAdapter.ActionDiscoveryFinished);
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                AndroidApp.Context.RegisterReceiver(_discoveryReceiver, filter, ReceiverFlags.Exported);
            else
                AndroidApp.Context.RegisterReceiver(_discoveryReceiver, filter);
            adapter.StartDiscovery();
        }
        catch (Java.Lang.SecurityException ex) { Services.DebugLog.Write($"[Radio] Bluetooth discovery not permitted: {ex.Message}"); }

        try
        {
            if (adapter.BluetoothLeScanner is { } le)
            {
                _leCallback = new LeCallback(this);
                var settings = new ScanSettings.Builder().SetScanMode(global::Android.Bluetooth.LE.ScanMode.Balanced)!.Build();
                le.StartScan(null, settings, _leCallback);
            }
        }
        catch (Java.Lang.SecurityException ex) { Services.DebugLog.Write($"[Radio] Bluetooth LE scan not permitted: {ex.Message}"); }
    }

    private void StopBluetooth()
    {
        var adapter = BluetoothAdapter();
        try { adapter?.CancelDiscovery(); } catch (Java.Lang.SecurityException) { }
        if (_discoveryReceiver is not null)
        {
            try { AndroidApp.Context.UnregisterReceiver(_discoveryReceiver); } catch { /* already gone */ }
            _discoveryReceiver = null;
        }
        if (_leCallback is not null)
        {
            try { adapter?.BluetoothLeScanner?.StopScan(_leCallback); } catch { /* adapter off */ }
            _leCallback = null;
        }
    }

    // Discovery ends by itself after about 12 s; start the next while we're running
    private void RestartDiscovery()
    {
        if (_btTimer is null) return;
        try { BluetoothAdapter()?.StartDiscovery(); } catch (Java.Lang.SecurityException) { }
    }

    private void AddBluetooth(BluetoothDevice device, int rssi, string? name, RadioNetworkType type, int? mfgrId, string? capabilitiesSuffix)
    {
        var mac = device.Address;
        if (string.IsNullOrEmpty(mac)) return;
        int deviceClass = 0;
        string? legend = null;
        try
        {
            name ??= device.Name;
            if (device.BluetoothClass is { } bc)
            {
                deviceClass = (int)bc.DeviceClass;
                legend = ClassName(bc);
            }
        }
        catch (Java.Lang.SecurityException) { /* Nearby devices refused: no name or class */ }

        lock (_btLock)
        {
            // Classic and LE reports of the same device merge; LE wins, as in WiGLE
            if (_btPending.TryGetValue(mac, out var seen))
            {
                seen.Rssi = Math.Max(seen.Rssi, rssi);
                if (type == RadioNetworkType.BLE) seen.Type = RadioNetworkType.BLE;
                if (!string.IsNullOrEmpty(name)) seen.Name = name;
                seen.MfgrId ??= mfgrId;
                return;
            }
            var now = DateTime.UtcNow;
            _btPending[mac] = new RadioNetwork
            {
                Key = mac.ToUpperInvariant(),
                Type = type,
                Name = name ?? string.Empty,
                Capabilities = (legend ?? string.Empty) + capabilitiesSuffix,
                Frequency = deviceClass,
                MfgrId = mfgrId,
                Rssi = rssi,
                FirstSeen = now,
                LastSeen = now,
                IsActive = true,
            };
        }
    }

    private void FlushBluetooth()
    {
        List<RadioNetwork> batch;
        lock (_btLock)
        {
            if (_btPending.Count == 0) return;
            batch = _btPending.Values.ToList();
            _btPending.Clear();
        }
        NetworksDetected?.Invoke(this, batch);
    }

    private sealed class BtDiscoveryReceiver(AndroidRadioScannerService owner) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == global::Android.Bluetooth.BluetoothAdapter.ActionDiscoveryFinished)
            {
                owner.RestartDiscovery();
                return;
            }
            if (intent?.Action != BluetoothDevice.ActionFound) return;
            var device = OperatingSystem.IsAndroidVersionAtLeast(33)
                ? intent.GetParcelableExtra(BluetoothDevice.ExtraDevice, Java.Lang.Class.FromType(typeof(BluetoothDevice))) as BluetoothDevice
#pragma warning disable CA1422
                : intent.GetParcelableExtra(BluetoothDevice.ExtraDevice) as BluetoothDevice;
#pragma warning restore CA1422
            if (device is null) return;
            int rssi = intent.GetShortExtra(BluetoothDevice.ExtraRssi, short.MinValue);
            if (rssi == short.MinValue) return;
            string bond = "";
            try { bond = ";" + (int)device.BondState; } catch (Java.Lang.SecurityException) { }
            owner.AddBluetooth(device, rssi, intent.GetStringExtra(BluetoothDevice.ExtraName), RadioNetworkType.BT, null, bond);
        }
    }

    private sealed class LeCallback(AndroidRadioScannerService owner) : ScanCallback
    {
        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result) => Handle(result);

        public override void OnBatchScanResults(IList<ScanResult>? results)
        {
            if (results is null) return;
            foreach (var r in results) Handle(r);
        }

        private void Handle(ScanResult? result)
        {
            if (result?.Device is not { } device) return;
            int? mfgr = null;
            var data = result.ScanRecord?.ManufacturerSpecificData;
            if (data is not null && data.Size() > 0) mfgr = data.KeyAt(data.Size() - 1);
            owner.AddBluetooth(device, result.Rssi, result.ScanRecord?.DeviceName, RadioNetworkType.BLE, mfgr, null);
        }
    }

    // WiGLE's DEVICE_TYPE_LEGEND, falling back to the major class when the minor one isn't listed
    private static string ClassName(BluetoothClass bc)
    {
        if (Legend.TryGetValue((int)bc.DeviceClass, out var name)) return name;
        return bc.MajorDeviceClass switch
        {
            MajorDeviceClass.AudioVideo => "A/V",
            MajorDeviceClass.Computer => "Computer",
            MajorDeviceClass.Health => "Health",
            MajorDeviceClass.Imaging => "Imaging",
            MajorDeviceClass.Networking => "Networking",
            MajorDeviceClass.Peripheral => "Peripheral",
            MajorDeviceClass.Phone => "Phone",
            MajorDeviceClass.Toy => "Toy",
            MajorDeviceClass.Wearable => "Wearable",
            MajorDeviceClass.Misc => "Misc",
            _ => "Uncategorized",
        };
    }

    private static readonly Dictionary<int, string> Legend = new()
    {
        [0] = "Misc",
        [(int)DeviceClass.AudioVideoCamcorder] = "Camcorder",
        [(int)DeviceClass.AudioVideoCarAudio] = "Car Audio",
        [(int)DeviceClass.AudioVideoHandsfree] = "Handsfree",
        [(int)DeviceClass.AudioVideoHeadphones] = "Headphones",
        [(int)DeviceClass.AudioVideoHifiAudio] = "HiFi",
        [(int)DeviceClass.AudioVideoLoudspeaker] = "Speaker",
        [(int)DeviceClass.AudioVideoMicrophone] = "Mic",
        [(int)DeviceClass.AudioVideoPortableAudio] = "Portable Audio",
        [(int)DeviceClass.AudioVideoSetTopBox] = "Settop",
        [(int)DeviceClass.AudioVideoUncategorized] = "A/V",
        [(int)DeviceClass.AudioVideoVcr] = "VCR",
        [(int)DeviceClass.AudioVideoVideoCamera] = "Camera",
        [(int)DeviceClass.AudioVideoVideoConferencing] = "Videoconf",
        [(int)DeviceClass.AudioVideoVideoDisplayAndLoudspeaker] = "Display/Speaker",
        [(int)DeviceClass.AudioVideoVideoGamingToy] = "AV Toy",
        [(int)DeviceClass.AudioVideoVideoMonitor] = "Monitor",
        [(int)DeviceClass.ComputerDesktop] = "Desktop",
        [(int)DeviceClass.ComputerHandheldPcPda] = "PDA",
        [(int)DeviceClass.ComputerLaptop] = "Laptop",
        [(int)DeviceClass.ComputerPalmSizePcPda] = "Palm",
        [(int)DeviceClass.ComputerServer] = "Server",
        [(int)DeviceClass.ComputerUncategorized] = "Computer",
        [(int)DeviceClass.ComputerWearable] = "Wearable Computer",
        [(int)DeviceClass.HealthBloodPressure] = "Blood Pressure",
        [(int)DeviceClass.HealthDataDisplay] = "Health Display",
        [(int)DeviceClass.HealthGlucose] = "Glucose",
        [(int)DeviceClass.HealthPulseOximeter] = "PulseOxy",
        [(int)DeviceClass.HealthPulseRate] = "Pulse",
        [(int)DeviceClass.HealthThermometer] = "Thermometer",
        [(int)DeviceClass.HealthUncategorized] = "Health",
        [(int)DeviceClass.HealthWeighing] = "Scale",
        [0x0540] = "Keyboard",
        [0x05C0] = "Keyboard+p",
        [0x0500] = "Keyboard !p",
        [0x0580] = "Pointer",
        [(int)DeviceClass.PhoneCellular] = "Cellphone",
        [(int)DeviceClass.PhoneCordless] = "Cordless Phone",
        [(int)DeviceClass.PhoneIsdn] = "ISDN",
        [(int)DeviceClass.PhoneModemOrGateway] = "Modem/GW",
        [(int)DeviceClass.PhoneSmart] = "Smartphone",
        [(int)DeviceClass.PhoneUncategorized] = "Phone",
        [(int)DeviceClass.ToyController] = "Controller",
        [(int)DeviceClass.ToyDollActionFigure] = "Doll",
        [(int)DeviceClass.ToyGame] = "Game",
        [(int)DeviceClass.ToyRobot] = "Robot",
        [(int)DeviceClass.ToyUncategorized] = "Toy",
        [(int)DeviceClass.ToyVehicle] = "Vehicle",
        [(int)DeviceClass.WearableGlasses] = "Glasses",
        [(int)DeviceClass.WearableHelmet] = "Helmet",
        [(int)DeviceClass.WearableJacket] = "Jacket",
        [(int)DeviceClass.WearablePager] = "Pager",
        [(int)DeviceClass.WearableUncategorized] = "Wearable",
        [(int)DeviceClass.WearableWristWatch] = "Watch",
        [(int)MajorDeviceClass.Uncategorized] = "Uncategorized",
    };
}
#endif
