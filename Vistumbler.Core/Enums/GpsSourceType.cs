namespace Vistumbler.Core.Models;

public enum GpsSourceType
{
    /// <summary>Device location API (MAUI Geolocation — works on all platforms)</summary>
    DeviceLocation,
    /// <summary>NMEA over serial/COM port (Windows only)</summary>
    SerialPort,
    /// <summary>NMEA over TCP socket</summary>
    TcpSocket
}
