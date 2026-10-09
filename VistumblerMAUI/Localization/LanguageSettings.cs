using System.Globalization;

namespace VistumblerMAUI.Localization;

/// <summary>
/// The app's language (Settings → Language): the device's language by default, or one chosen from those the app has
/// text for. Only the text changes; numbers and dates keep following the device's region.
/// </summary>
public static class LanguageSettings
{
    private const string Key = "App_Language";

    // The languages the original Vistumbler had translations for, which seed ours; English first
    private static readonly string[] Candidates =
    {
        "en", "bg", "cs", "da", "de", "el", "es", "fr", "it", "ja", "nb", "nl", "pl", "pt-BR", "ru", "sv", "tr", "zh-Hant",
    };

    /// <summary>The chosen language code, or empty for the device's own.</summary>
    public static string Code
    {
        get => Preferences.Get(Key, string.Empty);
        set => Preferences.Set(Key, value ?? string.Empty);
    }

    private static CultureInfo? _system;

    /// <summary>Sets the text language from the setting. Call at startup, before any page is created.</summary>
    public static void Apply()
    {
        _system ??= CultureInfo.CurrentUICulture;
        var culture = Code.Length > 0 ? new CultureInfo(Code) : _system;
        Loc.Culture = culture;   // what Loc.T reads; the thread culture below is for anything else that looks
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>The choices for the picker: the device's language, then each language the app has text for.</summary>
    public static IReadOnlyList<(string Code, string Name)> Choices()
    {
        var list = new List<(string, string)> { ("", Loc.T("Language_System")) };
        foreach (var code in Candidates)
        {
            var culture = new CultureInfo(code);
            if (!Loc.Has(culture)) continue;
            var name = culture.NativeName;
            list.Add((code, char.ToUpper(name[0], culture) + name[1..]));
        }
        return list;
    }
}
