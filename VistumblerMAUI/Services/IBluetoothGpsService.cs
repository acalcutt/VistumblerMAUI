using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>A paired Bluetooth device, as offered in Settings → GPS.</summary>
public record BluetoothGpsDevice(string Name, string Address)
{
    public override string ToString() => $"{Name} ({Address})";
}

/// <summary>
/// An external GPS receiver read over Bluetooth (classic serial port profile), sending NMEA sentences. The
/// <see cref="GpsRouterService"/> uses it for <see cref="GpsSource.BluetoothNmea"/>; only registered on Android.
/// </summary>
public interface IBluetoothGpsService : IGpsService
{
    /// <summary>The devices paired with this phone; asks for the Bluetooth permission first. Throws with the reason when it can't list them.</summary>
    Task<IReadOnlyList<BluetoothGpsDevice>> GetPairedDevicesAsync();
}
