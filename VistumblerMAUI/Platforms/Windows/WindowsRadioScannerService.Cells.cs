#if WINDOWS
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using VistumblerMAUI.Services;
using Windows.Devices.Enumeration;
using Windows.Networking.NetworkOperators;

namespace VistumblerMAUI.Platforms.Windows;

/// <summary>
/// Cell towers from mobile broadband modems, through Windows' API for them: the serving and neighbouring cells each
/// modem reports, polled every 5 s. A cell is kept only with its full id (a neighbour reported by its physical id
/// alone isn't), keyed as WiGLE and the Android scanner key it. 5G NR cells aren't in the Windows 10 API this app
/// builds against, so they aren't read.
/// </summary>
public sealed partial class WindowsRadioScannerService
{
    private static readonly TimeSpan CellPollInterval = TimeSpan.FromSeconds(5);
    private const int CellMinStrength = -113;   // as WiGLE and Android report a cell with no usable strength

    private readonly Task<IReadOnlyList<CellularModem>> _modemsAtStartup = Task.Run(FindModemsAsync);
    private readonly List<MobileBroadbandModem> _modems = new();
    private Timer? _cellTimer;
    private int _cellPolling;

    /// <summary>Whether there's a modem to read cells from (looked for at startup).</summary>
    public bool SupportsCells => _modemsAtStartup.Wait(DetectWait) && _modemsAtStartup.Result.Count > 0;

    private static async Task<IReadOnlyList<CellularModem>> FindModemsAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(MobileBroadbandModem.GetDeviceSelector());
            return devices.Select(d => new CellularModem(d.Id, d.Name)).ToList();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Radio] no mobile broadband: {ex.Message}");   // no service for it, or no access to it
            return [];
        }
    }

    // Started after the modems are listed again (one may have been plugged in since startup), unless Stop or another
    // Start came first
    private async Task StartCellsAsync(int generation)
    {
        var found = new List<MobileBroadbandModem>();
        foreach (var modem in await FindModemsAsync())
        {
            try
            {
                if (MobileBroadbandModem.FromId(modem.Id) is { } m) found.Add(m);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[Radio] modem {modem.Name}: {ex.Message}");   // unplugged since it was listed
            }
        }
        lock (_lock)
        {
            if (generation != Volatile.Read(ref _generation) || found.Count == 0) return;
            _modems.Clear();
            _modems.AddRange(found);
            _cellTimer = new Timer(_ => _ = PollCellsAsync(), null, TimeSpan.Zero, CellPollInterval);
        }
    }

    private void StopCells()
    {
        lock (_lock)
        {
            _cellTimer?.Dispose();
            _cellTimer = null;
            _modems.Clear();
        }
    }

    private async Task PollCellsAsync()
    {
        // A slow modem doesn't get a second poll stacked on the first
        if (Interlocked.Exchange(ref _cellPolling, 1) == 1) return;
        try
        {
            List<MobileBroadbandModem> modems;
            lock (_lock) modems = _modems.ToList();
            foreach (var modem in modems)
            {
                if (!_running) return;
                try
                {
                    var network = modem.CurrentNetwork;
                    if (network is null) continue;
                    var info = await network.GetCellsInfoAsync();
                    if (info is null) continue;
                    foreach (var n in ReadCells(info, network.RegisteredProviderId, network.RegisteredProviderName))
                        Heard(n);
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"[Radio] cell read failed: {ex.Message}");   // the modem's off, or doesn't report cells
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _cellPolling, 0);
        }
    }

    /// <summary>The cells the modem reports, as the Android scanner records them (see <see cref="RadioNetwork"/>).</summary>
    internal static IEnumerable<RadioNetwork> ReadCells(MobileBroadbandCellsInfo info, string? registeredId, string? registeredName)
    {
        string Operator(string providerId) => providerId == registeredId ? registeredName ?? string.Empty : string.Empty;

        foreach (var c in info.ServingCellsGsm.Concat(info.NeighboringCellsGsm))
            if (Gsmish(RadioNetworkType.GSM, c.ProviderId, c.LocationAreaCode, c.CellId, c.ChannelNumber, c.ReceivedSignalStrengthInDBm, Operator) is { } n)
                yield return n;

        foreach (var c in info.ServingCellsUmts.Concat(info.NeighboringCellsUmts))
            if (Gsmish(RadioNetworkType.WCDMA, c.ProviderId, c.LocationAreaCode, c.CellId, c.ChannelNumber, c.ReceivedSignalCodePowerInDBm, Operator) is { } n)
                yield return n;

        foreach (var c in info.ServingCellsLte.Concat(info.NeighboringCellsLte))
            if (Gsmish(RadioNetworkType.LTE, c.ProviderId, c.TrackingAreaCode, c.CellId, c.ChannelNumber, c.ReferenceSignalReceivedPowerInDBm, Operator) is { } n)
                yield return n;

        foreach (var c in info.ServingCellsCdma.Concat(info.NeighboringCellsCdma))
            if (c.SystemId is { } sid && c.NetworkId is { } nid && c.BaseStationId is { } bid && sid > 0 && nid >= 0 && bid > 0)
                yield return new RadioNetwork
                {
                    Key = $"{sid}_{nid}_{bid}",
                    Type = RadioNetworkType.CDMA,
                    Name = registeredName ?? string.Empty,
                    Capabilities = "CDMA;" + registeredId,
                    Rssi = Strength(c.PilotSignalStrengthInDB),
                };
    }

    // WiGLE's GsmOperator: "mccmnc_lac_cid", dropping cells whose identity the modem didn't fill in
    private static RadioNetwork? Gsmish(RadioNetworkType type, string? providerId, int? lacOrTac, int? cellId, int? channel,
        double? dbm, Func<string, string> operatorName)
    {
        // The provider id is the MCC and MNC run together, 5 or 6 digits
        if (providerId is not { Length: 5 or 6 } || !providerId.All(char.IsAsciiDigit)) return null;
        if (lacOrTac is not ({ } area and > 0 and < int.MaxValue)) return null;
        if (cellId is not ({ } cid and > 0 and < int.MaxValue)) return null;
        int? fcn = channel is > 0 and < int.MaxValue ? channel : null;
        return new RadioNetwork
        {
            Key = $"{providerId}_{area}_{cid}",
            Type = type,
            Name = operatorName(providerId),
            Capabilities = $"{type};{providerId}",
            Channel = fcn,
            Frequency = fcn ?? 0,
            Rssi = Strength(dbm),
        };
    }

    private static int Strength(double? dbm) => dbm is { } d && d < 0 ? (int)Math.Round(d) : CellMinStrength;
}
#endif
