#if WINDOWS
using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Platforms.Windows;

namespace VistumblerMAUI;

public static partial class MauiProgram
{
    static partial void RegisterPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<IWiFiScannerService, WindowsWiFiScannerService>();
        services.AddSingleton<ISerialGpsService,   SerialNmeaGpsService>();
    }
}
#endif
