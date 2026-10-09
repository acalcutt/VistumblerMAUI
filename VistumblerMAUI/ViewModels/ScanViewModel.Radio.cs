using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Cell towers and Bluetooth devices, recorded alongside the Wi-Fi scan when Settings → Scanning says to
/// (Android). They're kept apart from the APs, in their own tables and their own list, chosen with the type picker
/// on the Scan page.
/// </summary>
public partial class ScanViewModel
{
    private IRadioScannerService? _radio;
    private readonly Dictionary<string, RadioNetwork> _radioMap = new(StringComparer.OrdinalIgnoreCase);

    // Bluetooth devices come and go faster than APs; a cell stays "active" while it's in the cell list
    private const int RadioDeadAfterSeconds = 30;

    public const string WifiListName = "Wi-Fi", BluetoothListName = "Bluetooth", CellListName = "Cell towers";

    /// <summary>The lists the Scan page can show; only Wi-Fi when this device records neither of the others.</summary>
    public IReadOnlyList<string> ListTypes { get; private set; } = new[] { WifiListName };

    [ObservableProperty] private string _selectedListType = WifiListName;
    [ObservableProperty] private bool _isWifiList = true;
    [ObservableProperty] private bool _hasListTypes;
    [ObservableProperty] private int _bluetoothCount;
    [ObservableProperty] private int _cellCount;

    /// <summary>The Bluetooth devices or cells shown while one of those lists is chosen.</summary>
    public ObservableCollection<RadioNetwork> RadioNetworks { get; } = new();

    /// <summary>Everything recorded this session, for exports.</summary>
    public IReadOnlyCollection<RadioNetwork> AllRadioNetworks => _radioMap.Values;

    private void InitRadio(IRadioScannerService radio)
    {
        _radio = radio;
        var types = new List<string> { WifiListName };
        if (radio.SupportsBluetooth) types.Add(BluetoothListName);
        if (radio.SupportsCells) types.Add(CellListName);
        ListTypes = types;
        HasListTypes = types.Count > 1;
        radio.NetworksDetected += OnRadioNetworksDetected;
        Services.ScanSettings.RadioChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (IsScanning) StartRadio();
        });
    }

    partial void OnSelectedListTypeChanged(string value)
    {
        IsWifiList = value == WifiListName;
        RebuildRadioList();
    }

    // Which of the Scan page's three lists shows: flat or grouped APs, or Bluetooth/cells
    public bool ShowFlatWifiList => IsWifiList && !IsGrouped;
    public bool ShowGroupedWifiList => IsWifiList && IsGrouped;
    partial void OnIsWifiListChanged(bool value) => NotifyListVisibility();
    partial void OnIsGroupedChanged(bool value) => NotifyListVisibility();

    private void NotifyListVisibility()
    {
        OnPropertyChanged(nameof(ShowFlatWifiList));
        OnPropertyChanged(nameof(ShowGroupedWifiList));
    }

    private void StartRadio()
    {
        if (_radio is null) return;
        bool cells = Services.ScanSettings.ScanCells && _radio.SupportsCells;
        bool bluetooth = Services.ScanSettings.ScanBluetooth && _radio.SupportsBluetooth;
        if (cells || bluetooth) _radio.Start(cells, bluetooth);
        else _radio.Stop();
    }

    private void StopRadio() => _radio?.Stop();

    private async Task LoadRadioAsync()
    {
        foreach (var n in await _db.GetAllRadioNetworksAsync())
        {
            n.IsActive = false;
            _radioMap[n.Key] = n;
        }
        UpdateRadioCounts();
        RebuildRadioList();
    }

    private void ClearRadioInMemory()
    {
        _radioMap.Clear();
        UpdateRadioCounts();
        RebuildRadioList();
    }

    // Background thread: merge on the UI thread, then persist like a Wi-Fi scan cycle
    private async void OnRadioNetworksDetected(object? sender, IReadOnlyList<RadioNetwork> heard)
    {
        var now = DateTime.UtcNow;
        var gps = _currentGps;
        var toPersist = new List<RadioNetwork>(heard.Count);
        int generation = 0;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            generation = _clearGeneration;
            foreach (var n in heard)
            {
                if (_radioMap.TryGetValue(n.Key, out var known))
                {
                    known.Rssi = n.Rssi;
                    known.LastSeen = now;
                    known.IsActive = true;
                    if (!string.IsNullOrEmpty(n.Name)) known.Name = n.Name;
                    if (known.Type == RadioNetworkType.BT && n.Type == RadioNetworkType.BLE) known.Type = n.Type;   // as WiGLE
                    if (string.IsNullOrEmpty(known.Capabilities) || known.Capabilities.StartsWith("Misc", StringComparison.Ordinal))
                        known.Capabilities = n.Capabilities;
                    known.Channel = n.Channel ?? known.Channel;
                    if (n.Frequency != 0) known.Frequency = n.Frequency;
                    known.MfgrId ??= n.MfgrId;
                    if (n.Rssi >= known.HighestRssi)
                    {
                        known.HighestRssi = n.Rssi;
                        if (gps is not null) { known.Latitude = gps.Latitude; known.Longitude = gps.Longitude; }
                    }
                    toPersist.Add(known);
                }
                else
                {
                    n.FirstSeen = n.LastSeen = now;
                    n.HighestRssi = n.Rssi;
                    n.IsActive = true;
                    if (n.IsBluetooth) n.Manufacturer = _manufacturers.Lookup(n.Key);
                    if (gps is not null) { n.Latitude = gps.Latitude; n.Longitude = gps.Longitude; }
                    _radioMap[n.Key] = n;
                    toPersist.Add(n);
                }
            }

            var deadCutoff = now.AddSeconds(-RadioDeadAfterSeconds);
            foreach (var n in _radioMap.Values)
                if (n.IsActive && n.LastSeen < deadCutoff) n.IsActive = false;

            UpdateRadioCounts();
            RebuildRadioList();
        });

        await _persistLock.WaitAsync();
        try
        {
            if (generation != _clearGeneration) return;   // cleared meanwhile
            await _db.SaveRadioCycleAsync(toPersist, gps, now);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScanVM] SaveRadioCycleAsync failed (cycle dropped): {ex.Message}");
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private void UpdateRadioCounts()
    {
        BluetoothCount = _radioMap.Values.Count(n => n.IsBluetooth);
        CellCount = _radioMap.Count - BluetoothCount;
    }

    /// <summary>Brings the Bluetooth or cell list in line with what's recorded: searched, strongest active first. UI thread.</summary>
    private void RebuildRadioList()
    {
        if (IsWifiList)
        {
            if (RadioNetworks.Count > 0) RadioNetworks.Clear();
            return;
        }
        bool bluetooth = SelectedListType == BluetoothListName;
        var q = SearchText?.Trim() ?? string.Empty;
        var desired = _radioMap.Values
            .Where(n => n.IsBluetooth == bluetooth)
            .Where(n => q.Length == 0
                        || n.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || n.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || n.Manufacturer.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.IsActive)
            .ThenByDescending(n => n.IsActive ? n.Rssi : n.HighestRssi)
            .ThenBy(n => n.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var wanted = new HashSet<RadioNetwork>(desired);
        for (int i = RadioNetworks.Count - 1; i >= 0; i--)
            if (!wanted.Contains(RadioNetworks[i])) RadioNetworks.RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < RadioNetworks.Count && ReferenceEquals(RadioNetworks[i], desired[i])) continue;
            int existing = RadioNetworks.IndexOf(desired[i]);
            if (existing >= 0) RadioNetworks.Move(existing, i);
            else RadioNetworks.Insert(i, desired[i]);
        }
    }
}
