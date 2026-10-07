using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// An external GPS receiver plugged in over USB (through an OTG adapter), sending NMEA through a USB serial chip:
/// Prolific PL2303 (GlobalSat), Silicon Labs CP210x, FTDI, WCH CH34x or CDC-ACM (u-blox). The
/// <see cref="GpsRouterService"/> uses it for <see cref="GpsSource.UsbNmea"/>; only registered on Android. It uses
/// the first supported device that is plugged in.
/// </summary>
public interface IUsbGpsService : IGpsService
{
    /// <summary>The supported USB serial devices plugged in now, described for Settings (e.g. "Prolific PL2303 (067B:2303)").</summary>
    IReadOnlyList<string> GetAttachedDevices();
}
