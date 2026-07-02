namespace Vistumbler.Core.Services;

/// <summary>
/// A serial/NMEA-backed GPS source (COM port). Only registered on platforms with serial
/// ports (Windows); the GPS router resolves it optionally and falls back to the platform
/// location service where it isn't available.
/// </summary>
public interface ISerialGpsService : IGpsService
{
    /// <summary>Names of the serial ports currently available (e.g. "COM3").</summary>
    string[] GetAvailablePorts();
}
