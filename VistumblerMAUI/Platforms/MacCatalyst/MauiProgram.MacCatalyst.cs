#if MACCATALYST
using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Platforms.MacCatalyst;
using VistumblerMAUI.Services;

namespace VistumblerMAUI;

public static partial class MauiProgram
{
    static partial void RegisterPlatformServices(IServiceCollection services)
    {
        // CoreWLAN through the Objective-C runtime; needs Location Services permission for names and BSSIDs
        services.AddSingleton<IWiFiScannerService, MacWiFiScannerService>();
        services.AddSingleton<ILocationGpsService, MauiGeolocationGpsService>();
        services.AddSingleton<IKeepAliveService,   NullKeepAliveService>();
    }
}
#endif
