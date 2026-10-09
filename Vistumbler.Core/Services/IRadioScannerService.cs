using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Scans for cell towers and Bluetooth devices alongside the Wi-Fi scan. Android only for now; elsewhere
/// <see cref="SupportsCells"/> and <see cref="SupportsBluetooth"/> are false and nothing is reported.
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

    /// <summary>Starts whichever of cells and Bluetooth are asked for; permissions must already be granted.</summary>
    void Start(bool cells, bool bluetooth);
    void Stop();
}

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
