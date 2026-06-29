using System.Diagnostics;
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
        services.AddSingleton<IDatabaseService,  SqliteDatabaseService>();
        services.AddSingleton<IGpsService,       MauiGeolocationGpsService>();
        services.AddSingleton<ISoundService,     MauiSoundService>();
        services.AddSingleton<IExportService,    ExportService>();
        services.AddSingleton<IImportService,    ImportService>();

        // Platform WiFi scanner registered in platform-specific startup
        RegisterPlatformServices(services);

        // ── ViewModels ───────────────────────────────────────────────────────
        services.AddSingleton<ScanViewModel>();
        services.AddSingleton<MapViewModel>();
        services.AddTransient<AccessPointListViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ImportViewModel>();
        services.AddTransient<ExportViewModel>();

        // ── Pages ────────────────────────────────────────────────────────────
        services.AddTransient<ScanPage>();
        services.AddTransient<MapPage>();
        services.AddTransient<AccessPointListPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<ImportPage>();
        services.AddTransient<ExportPage>();

        Debug.WriteLine("[MauiProgram] CreateMauiApp EXIT (build)");
        return builder.Build();
    }

    static partial void RegisterPlatformServices(IServiceCollection services);
}
