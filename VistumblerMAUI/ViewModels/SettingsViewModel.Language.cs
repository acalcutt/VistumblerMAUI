using CommunityToolkit.Mvvm.ComponentModel;
using VistumblerMAUI.Localization;

namespace VistumblerMAUI.ViewModels;

/// <summary>Settings → Language: the phone's language, or one the app has text for. The app is redrawn in it at once.</summary>
public partial class SettingsViewModel
{
    private IReadOnlyList<(string Code, string Name)>? _languages;
    private IReadOnlyList<(string Code, string Name)> Languages => _languages ??= LanguageSettings.Choices();

    public IReadOnlyList<string> LanguageNames => Languages.Select(l => l.Name).ToList();

    public string SelectedLanguageName
    {
        get => Languages.FirstOrDefault(l => l.Code == LanguageSettings.Code).Name ?? Languages[0].Name;
        set
        {
            var choice = Languages.FirstOrDefault(l => l.Name == value);
            if (choice.Name is null || choice.Code == LanguageSettings.Code) return;
            LanguageSettings.Code = choice.Code;
            OnPropertyChanged();
            // Rebuild the pages in the new language once the picker has closed; scanning and the session carry on
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                LanguageSettings.Apply();
                if (Application.Current?.Windows.FirstOrDefault() is not { } window) return;
                var services = IPlatformApplication.Current?.Services;
                if (services?.GetService(typeof(AppShell)) is not AppShell shell) return;
                window.Page = shell;
                await shell.GoToAsync("//SettingsPage");
            });
        }
    }
}
