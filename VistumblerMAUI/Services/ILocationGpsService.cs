using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// The OS location back-end <see cref="GpsRouterService"/> routes to when the user selects
/// the "location service" GPS source (vs serial NMEA). Registered per platform:
/// Android uses <c>AndroidGpsService</c> (LocationManager fused provider — MAUI's
/// Geolocation foreground listener delivers a single fix and then goes silent on some
/// devices); Windows/iOS use <see cref="MauiGeolocationGpsService"/>.
/// </summary>
public interface ILocationGpsService : IGpsService
{
}
