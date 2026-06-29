#if IOS
using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Platforms.iOS;

namespace VistumblerMAUI;

public static partial class MauiProgram
{
    static partial void RegisterPlatformServices(IServiceCollection services)
    {
        // iOS does not allow full WiFi scanning; stub returns empty list.
        services.AddSingleton<IWiFiScannerService, iOSWiFiScannerService>();
    }
}
#endif
