using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Views;

namespace VistumblerMAUI;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var session = _services.GetRequiredService<ISessionService>();

        // If prior sessions exist, let the user recover/remove one or start new; otherwise
        // begin a fresh session straight away.
        Page start;
        if (session.ListSessions().Count > 0)
        {
            start = _services.GetRequiredService<SessionChooserPage>();
        }
        else
        {
            session.StartNewSession();
            start = _services.GetRequiredService<AppShell>();
        }

        return new Window(start);
    }
}
