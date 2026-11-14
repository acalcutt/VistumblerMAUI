using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Scans for cell towers and Bluetooth devices alongside the Wi-Fi scan, on Android and Windows (cell towers there
/// from a mobile broadband modem, which most PCs don't have). Elsewhere <see cref="SupportsCells"/> and
/// <see cref="SupportsBluetooth"/> are false and nothing is reported.
/// </summary>
public interface IRadioScannerService
{
    /// <summary>Raised with the networks heard in each cell poll or Bluetooth batch, on a background thread.</summary>
    event EventHandler<IReadOnlyList<RadioNetwork>>? NetworksDetected;

    bool SupportsCells { get; }
    bool SupportsBluetooth { get; }
    bool IsRunning { get; }

    /// <summary>Asks for what scanning them needs (location; Nearby devices for Bluetooth); false if refused.</summary>
    Task<bool> RequestPermissionsAsync(bool cells, bool bluetooth);

    /// <summary>
    /// The cellular modems cells can be read from, to choose among (Windows). None where cells come from the device's
    /// own phone radio, as on Android, or where there's no modem.
    /// </summary>
    Task<IReadOnlyList<CellularModem>> GetCellularModemsAsync() => Task.FromResult<IReadOnlyList<CellularModem>>([]);

    /// <summary>The modems to read cells from at the next <see cref="Start"/>, by id; empty reads every one.</summary>
    void SetCellularModems(IReadOnlyCollection<string> modemIds) { }

    /// <summary>Starts whichever of cells and Bluetooth are asked for; permissions must already be granted.</summary>
    void Start(bool cells, bool bluetooth);
    void Stop();
}

/// <summary>A cellular modem to read cell towers from (Windows).</summary>
public sealed record CellularModem(string Id, string Name);

/// <summary>For platforms with neither.</summary>
public sealed class NoRadioScannerService : IRadioScannerService
{
    public event EventHandler<IReadOnlyList<RadioNetwork>>? NetworksDetected { add { } remove { } }
    public bool SupportsCells => false;
    public bool SupportsBluetooth => false;
    public bool IsRunning => false;
    public Task<bool> RequestPermissionsAsync(bool cells, bool bluetooth) => Task.FromResult(false);
    public void Start(bool cells, bool bluetooth) { }
    public void Stop() { }
}
