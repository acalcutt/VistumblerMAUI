using System.Globalization;
using System.Resources;

namespace VistumblerMAUI.Localization;

/// <summary>
/// The app's text in the chosen language, from Resources/Strings/AppResources*.resx. English is the default and the
/// fallback: a string a language doesn't have yet shows in English. In XAML use <c>{loc:T Key}</c>; in code
/// <c>Loc.T("Key")</c>, or <c>Loc.T("Key", args)</c> for text with {0} placeholders.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Strings =
        new("VistumblerMAUI.Resources.Strings.AppResources", typeof(Loc).Assembly);

    /// <summary>
    /// The language text is shown in (<see cref="LanguageSettings.Apply"/>). Kept here rather than read from
    /// CultureInfo.CurrentUICulture, which follows the async flow it was set in: a page built later, e.g. from a tab
    /// tap, would get the device's language again.
    /// </summary>
    public static CultureInfo Culture { get; set; } = CultureInfo.CurrentUICulture;

    /// <summary>The text for <paramref name="key"/>; the key itself when there's none, so a missing one is easy to spot.</summary>
    public static string T(string key) => Strings.GetString(key, Culture) ?? key;

    public static string T(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>
    /// A choice's text in the chosen language, while the code keeps the English value ("No grouping", "Signal (%)"):
    /// the key is "Opt_" + the value's letters and digits, with % as "Pct". Falls back to the value itself.
    /// </summary>
    public static string Opt(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var key = "Opt_" + string.Concat(value.Select(c => c == '%' ? "Pct" : char.IsLetterOrDigit(c) ? c.ToString() : ""));
        var text = T(key);
        return text == key ? value : text;
    }

    /// <summary>Whether the app has text of its own in <paramref name="culture"/> (not just the English fallback).</summary>
    public static bool Has(CultureInfo culture)
    {
        if (culture.TwoLetterISOLanguageName == "en") return true;
        try { return Strings.GetResourceSet(culture, createIfNotExists: true, tryParents: false) is not null; }
        catch (MissingManifestResourceException) { return false; }
    }
}
