using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Localization;
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
        Routing.RegisterRoute(nameof(SurveyPage), typeof(SurveyPage));
        Routing.RegisterRoute(nameof(FiltersPage), typeof(FiltersPage));
        Routing.RegisterRoute(nameof(FilterEditPage), typeof(FilterEditPage));
        Routing.RegisterRoute(nameof(MapColorsPage), typeof(MapColorsPage));
        Routing.RegisterRoute(nameof(ApDetailsPage), typeof(ApDetailsPage));

        Loaded += OnFirstLoaded;
    }

    private static bool _started;

    // Like the original Vistumbler: offer a newer release at startup (quietly does nothing when offline)
    private async void OnFirstLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnFirstLoaded;
        // Once per run: the shell is built again when the language changes (Settings → Language)
        if (_started) return;
        _started = true;
        // Retry WifiDB uploads left over from earlier saves, now and whenever the connection comes back
        _services.GetRequiredService<WifiDbUploadQueue>().Start();
        _services.GetRequiredService<WigleUploadQueue>().Start();   // the same for WiGLE, when it's turned on
        // The original's "Auto Scan APs on launch", plus the same for GPS (Settings → Scanning)
        await _services.GetRequiredService<ScanViewModel>().StartOnLaunchAsync();
        // Once the session (resumed automatically or not) has run a few seconds without crashing, an automatic
        // resume of it is safe again; see App.AutoResumeKey
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => Preferences.Remove(App.AutoResumeKey));
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

    // The original's Clear All and Save & Clear in one: clear the AP list, offering to save it to a file first
    // (Settings → Save & Clear sets where). Scanning carries on either way.
    private async void OnClearClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        string save = Loc.T("Clear_SaveThenClear"), discard = Loc.T("Clear_WithoutSaving");
        var (folder, _) = SaveAndClearSettings.Resolve();
        var choice = await DisplayActionSheet(
            Loc.T("Clear_Question", SaveFolder.Describe(folder)),
            Loc.T("Common_Cancel"), discard, save);
        if (choice == save)
            await SaveAndClearAsync();
        else if (choice == discard)
            await _services.GetRequiredService<ScanViewModel>().ClearAllCommand.ExecuteAsync(null);
    }

    // Keep the scan in a file, then start the list over without stopping the scan
    private async Task SaveAndClearAsync()
    {
        var result = await _services.GetRequiredService<ScanViewModel>().SaveAndClearAsync();
        if (result.Path is null)
        {
            await DisplayAlert(Loc.T("Clear_SaveAndClear"), result.Message, Loc.T("Common_Ok"));
            return;
        }
        // App-private folders (the Android default) can't be reached from a file manager, so offer to share
        if (await DisplayAlert(Loc.T("Clear_SaveAndClear"), result.Message, Loc.T("Common_Share"), Loc.T("Common_Ok")))
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

    // Site survey (tap-to-mark) on a floor plan, for indoors
    private async void OnSurveyClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        await GoToAsync(nameof(SurveyPage));
    }

    // The original's Help, WifiDB and Support Vistumbler web pages, in one list
    private async void OnLinksClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        var links = AppLinks.All;
        var choice = await DisplayActionSheet(Loc.T("Links_Title"), Loc.T("Common_Cancel"), null, links.Select(l => l.Title).ToArray());
        var url = links.FirstOrDefault(l => l.Title == choice).Url;
        if (url is null) return;
        try { await Launcher.Default.OpenAsync(new Uri(url)); }
        catch (Exception ex) { await DisplayAlert(Loc.T("Links_Title"), Loc.T("Links_CouldNotOpen", url, ex.Message), Loc.T("Common_Ok")); }
    }

    private async void OnUploadToWifiDbClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        await GoToAsync(nameof(WifiDbUploadPage));
    }

    private async void OnNewSessionClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        bool ok = await DisplayAlert(Loc.T("NewSession_Title"),
            Loc.T("NewSession_Question"),
            Loc.T("Menu_NewSession"), Loc.T("Common_Cancel"));
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
        bool ok = await DisplayAlert(Loc.T("Exit_DiscardTitle"),
            Loc.T("Exit_DiscardQuestion"), Loc.T("Exit_Discard"), Loc.T("Common_Cancel"));
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
