using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Startup session chooser: lists prior capture sessions (each its own database) and lets
/// the user recover one, delete one, or start a fresh session — mirroring the original
/// VistumblerMDB's "recover/remove existing databases or create a new session" prompt.
/// </summary>
public partial class SessionChooserViewModel : ObservableObject
{
    private readonly ISessionService _session;
    private readonly IServiceProvider _services;

    public ObservableCollection<SessionInfo> Sessions { get; } = new();

    [ObservableProperty] private bool _hasSessions;

    public SessionChooserViewModel(ISessionService session, IServiceProvider services)
    {
        _session  = session;
        _services = services;
        Reload();
    }

    private void Reload()
    {
        Sessions.Clear();
        foreach (var s in _session.ListSessions())
            Sessions.Add(s);
        HasSessions = Sessions.Count > 0;
    }

    [RelayCommand]
    private void Resume(SessionInfo session)
    {
        if (session is null) return;
        _session.ResumeSession(session.Path);
        EnterApp();
    }

    [RelayCommand]
    private async Task Delete(SessionInfo session)
    {
        if (session is null) return;
        bool ok = await AppShellDisplayAlert("Delete session",
            $"Delete session '{session.Name}'? This cannot be undone.", "Delete", "Cancel");
        if (!ok) return;
        _session.DeleteSession(session.Path);
        Reload();
    }

    [RelayCommand]
    private void NewSession()
    {
        _session.StartNewSession();
        EnterApp();
    }

    // Swap the window's page to the main shell for the chosen session.
    private void EnterApp()
    {
        var shell = _services.GetRequiredService<AppShell>();
        if (Application.Current?.Windows.Count > 0)
            Application.Current.Windows[0].Page = shell;
    }

    private static Task<bool> AppShellDisplayAlert(string title, string message, string accept, string cancel)
    {
        var page = Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0].Page : null;
        return page is not null ? page.DisplayAlert(title, message, accept, cancel) : Task.FromResult(false);
    }
}
