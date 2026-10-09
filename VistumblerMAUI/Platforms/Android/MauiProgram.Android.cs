#if ANDROID
using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Platforms.Android;
using VistumblerMAUI.Services;

namespace VistumblerMAUI;

public static partial class MauiProgram
{
    static partial void RegisterPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<IWiFiScannerService,  AndroidWiFiScannerService>();
        services.AddSingleton<ILocationGpsService,  AndroidGpsService>();
        services.AddSingleton<IBluetoothGpsService, BluetoothNmeaGpsService>();
        services.AddSingleton<IUsbGpsService,       UsbNmeaGpsService>();
        services.AddSingleton<IKeepAliveService,    AndroidKeepAliveService>();
        services.AddSingleton<IRadioScannerService, AndroidRadioScannerService>();
    }
}
#endif
