using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Views;

namespace VistumblerMAUI;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// The session being resumed automatically, until the app has finished loading it. Still set at the next
    /// launch means that resume crashed the app, so the chooser is shown instead of trying it again.
    /// </summary>
    public const string AutoResumeKey = "Session_AutoResumePending";

    public App(IServiceProvider services)
    {
        // The text language from Settings → Language, before App.xaml or any page reads its text
        Localization.LanguageSettings.Apply();
        InitializeComponent();
        _services = services;
        // Coordinate format and Wi-Fi adapter from Settings, before anything is shown or scanned
        Services.ScanSettings.Apply(services.GetRequiredService<IWiFiScannerService>());
        // Manufacturer names (about 40,000) load in the background; scans fill them in once loaded
        _ = services.GetRequiredService<Services.ManufacturerDatabase>().LoadAsync();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var session = _services.GetRequiredService<ISessionService>();

        // A single earlier session (the app was exited, or closed without meaning to) is picked up where it
        // left off. Several, or one whose automatic resume crashed the app last time, go to the chooser to
        // recover, remove or start new; none starts a fresh session straight away.
        Page start;
        var sessions = session.ListSessions();
        var pending = Preferences.Get(AutoResumeKey, string.Empty);
        if (sessions.Count == 1 && pending != sessions[0].Path)
        {
            Preferences.Set(AutoResumeKey, sessions[0].Path);   // cleared once the app has loaded (AppShell)
            session.ResumeSession(sessions[0].Path);
            start = _services.GetRequiredService<AppShell>();
        }
        else if (sessions.Count > 0)
        {
            Preferences.Remove(AutoResumeKey);
            start = _services.GetRequiredService<SessionChooserPage>();
        }
        else
        {
            session.StartNewSession();
            start = _services.GetRequiredService<AppShell>();
        }

        return new Window(start) { Title = "VistumblerMAUI" };
    }
}
