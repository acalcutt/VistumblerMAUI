using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Services;
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
        Routing.RegisterRoute(nameof(WifiDbUploadPage), typeof(WifiDbUploadPage));
        Routing.RegisterRoute(nameof(GpsDetailsPage), typeof(GpsDetailsPage));
        Routing.RegisterRoute(nameof(ApDetailsPage), typeof(ApDetailsPage));

        Loaded += OnFirstLoaded;
    }

    // Like the original Vistumbler: offer a newer release at startup (quietly does nothing when offline)
    private async void OnFirstLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnFirstLoaded;
        // Retry WifiDB uploads left over from earlier saves, now and whenever the connection comes back
        _services.GetRequiredService<WifiDbUploadQueue>().Start();
        // The original's "Auto Scan APs on launch", plus the same for GPS (Settings → Scanning)
        await _services.GetRequiredService<ScanViewModel>().StartOnLaunchAsync();
        await _services.GetRequiredService<AppUpdater>().CheckOnStartupAsync(ExitForUpdateAsync);
    }

    private async void OnCheckForUpdatesClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        await CheckForUpdatesAsync();
    }

    /// <summary>Checks for a newer release on request (menu, Settings → Updates).</summary>
    public Task CheckForUpdatesAsync() =>
        _services.GetRequiredService<AppUpdater>().CheckAsync(interactive: true, ExitForUpdateAsync);

    // The Windows installer replaces the app's files: exit like "Exit (Save DB)" so the session can be resumed
    private Task ExitForUpdateAsync() => ShutdownAsync(discard: false);

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

    // Like the original Vistumbler's Save & Clear button: keep the scan in a file, then start the list
    // over without stopping the scan
    private async void OnSaveAndClearClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        var (folder, _) = SaveAndClearSettings.Resolve();
        bool ok = await DisplayAlert("Save & Clear",
            $"Save the access points to a file in {SaveFolder.Describe(folder)}, then clear the list? Scanning carries on.",
            "Save & Clear", "Cancel");
        if (!ok) return;

        var result = await _services.GetRequiredService<ScanViewModel>().SaveAndClearAsync();
        if (result.Path is null)
        {
            await DisplayAlert("Save & Clear", result.Message, "OK");
            return;
        }
        // App-private folders (the Android default) can't be reached from a file manager, so offer to share
        if (await DisplayAlert("Save & Clear", result.Message, "Share", "OK"))
        {
            try
            {
                // Sharing needs a real file; one in a folder picked on Android is copied to the cache for it
                var (local, _) = await SaveFolder.GetLocalFileAsync(result.Path);
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "VistumblerMAUI save",
                    File  = new ShareFile(local),
                });
            }
            catch { /* the file is saved either way */ }
        }
    }

    private async void OnGpsDetailsClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        await GoToAsync(nameof(GpsDetailsPage));
    }

    // The original's Help, WifiDB and Support Vistumbler web pages, in one list
    private async void OnLinksClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        var links = AppLinks.All;
        var choice = await DisplayActionSheet("Links", "Cancel", null, links.Select(l => l.Title).ToArray());
        var url = links.FirstOrDefault(l => l.Title == choice).Url;
        if (url is null) return;
        try { await Launcher.Default.OpenAsync(new Uri(url)); }
        catch (Exception ex) { await DisplayAlert("Links", $"Couldn't open {url}: {ex.Message}", "OK"); }
    }

    private async void OnUploadToWifiDbClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        await GoToAsync(nameof(WifiDbUploadPage));
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
