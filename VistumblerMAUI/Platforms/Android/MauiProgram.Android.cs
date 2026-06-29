#if ANDROID
using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Platforms.Android;

namespace VistumblerMAUI;

public static partial class MauiProgram
{
    static partial void RegisterPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<IWiFiScannerService, AndroidWiFiScannerService>();
    }
}
#endif
