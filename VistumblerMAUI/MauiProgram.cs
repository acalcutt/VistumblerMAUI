using System.Diagnostics;
using BarcodeScanning;
using CommunityToolkit.Maui;
using MapLibreNative.Maui.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Services;
using VistumblerMAUI.ViewModels;
using VistumblerMAUI.Views;

namespace VistumblerMAUI;

public static partial class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Debug.WriteLine("[MauiProgram] CreateMauiApp ENTER");
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseBarcodeScanning()
            .ConfigureMauiHandlers(handlers =>
            {
                Debug.WriteLine("[MauiProgram] Registering MapLibreMap -> MapLibreMapHandler");
                handlers.AddHandler(typeof(MapLibreMap), typeof(MapLibreMapHandler));
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf",    "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf",   "OpenSansSemibold");
            });

        var services = builder.Services;

        // ── Infrastructure services ──────────────────────────────────────────
        services.AddSingleton<ISessionService,   SessionService>();
        services.AddSingleton<IDatabaseService,  SqliteDatabaseService>();
        // ILocationGpsService (the OS location back-end) is registered per platform in
        // RegisterPlatformServices — Android uses LocationManager's fused provider directly.
        services.AddSingleton<IGpsService,       GpsRouterService>();     // selects location vs serial
        services.AddSingleton<ISoundService,     MauiSoundService>();
        services.AddSingleton<IExportService,    ExportService>();
        services.AddSingleton<IImportService,    ImportService>();

        // Platform WiFi scanner registered in platform-specific startup
        RegisterPlatformServices(services);

        // ── ViewModels ───────────────────────────────────────────────────────
        services.AddSingleton<ScanViewModel>();
        services.AddSingleton<MapViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ImportViewModel>();
        services.AddTransient<ExportViewModel>();
        services.AddTransient<ApDetailsViewModel>();
        services.AddTransient<SessionChooserViewModel>();
        services.AddTransient<ChannelGraphViewModel>();

        // ── Shell + Pages ─────────────────────────────────────────────────────
        services.AddTransient<AppShell>();
        services.AddTransient<SessionChooserPage>();
        services.AddTransient<ScanPage>();
        services.AddTransient<MapPage>();
        services.AddTransient<ChannelGraphPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<ImportPage>();
        services.AddTransient<ExportPage>();
        services.AddTransient<WifiDbScanPage>();
        services.AddTransient<ApDetailsPage>();

        Debug.WriteLine("[MauiProgram] CreateMauiApp EXIT (build)");
        return builder.Build();
    }

    static partial void RegisterPlatformServices(IServiceCollection services);
}
