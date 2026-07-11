#if IOS
using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Platforms.iOS;
using VistumblerMAUI.Services;

namespace VistumblerMAUI;

public static partial class MauiProgram
{
    static partial void RegisterPlatformServices(IServiceCollection services)
    {
        // iOS does not allow full WiFi scanning; stub returns empty list.
        services.AddSingleton<IWiFiScannerService, iOSWiFiScannerService>();
        services.AddSingleton<ILocationGpsService, MauiGeolocationGpsService>();
        services.AddSingleton<IKeepAliveService,   NullKeepAliveService>();
    }
}
#endif
