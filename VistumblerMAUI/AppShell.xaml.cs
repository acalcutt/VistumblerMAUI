using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.ViewModels;
using VistumblerMAUI.Views;

namespace VistumblerMAUI;

public partial class AppShell : Shell
{
    private readonly IServiceProvider _services;

    public AppShell(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;

        // Routes reached via the hamburger menu / details navigation.
        Routing.RegisterRoute(nameof(ImportPage), typeof(ImportPage));
        Routing.RegisterRoute(nameof(ExportPage), typeof(ExportPage));
        Routing.RegisterRoute(nameof(WifiDbScanPage), typeof(WifiDbScanPage));
        Routing.RegisterRoute(nameof(ApDetailsPage), typeof(ApDetailsPage));
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        await GoToAsync(nameof(ImportPage));
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        await GoToAsync(nameof(ExportPage));
    }

    private async void OnNewSessionClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        bool ok = await DisplayAlert("New session",
            "Start a new session? The current session stays saved and can be reopened later.",
            "New Session", "Cancel");
        if (!ok) return;

        var scan    = _services.GetRequiredService<ScanViewModel>();
        var map     = _services.GetRequiredService<MapViewModel>();
        var session = _services.GetRequiredService<ISessionService>();

        await scan.ResetForNewSessionAsync();   // stop scan/GPS, close DB, clear in-memory
        map.ResetForNewSession();
        session.StartNewSession();               // fresh timestamped DB path
        await scan.LoadCommand.ExecuteAsync(null); // opens + loads the new (empty) session DB
        await GoToAsync("//ScanPage");
    }

    private async void OnExitSaveClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        await ShutdownAsync(discard: false);
    }

    private async void OnExitDiscardClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        bool ok = await DisplayAlert("Exit without saving",
            "Discard this session's captured data and exit?", "Discard & Exit", "Cancel");
        if (!ok) return;
        await ShutdownAsync(discard: true);
    }

    private async Task ShutdownAsync(bool discard)
    {
        var scan    = _services.GetRequiredService<ScanViewModel>();
        var db      = _services.GetRequiredService<IDatabaseService>();
        var session = _services.GetRequiredService<ISessionService>();

        await scan.ResetForNewSessionAsync();  // stops scanning/GPS and closes the DB
        if (discard) session.DiscardCurrent();  // delete this session's file

        Application.Current?.Quit();
    }
}
