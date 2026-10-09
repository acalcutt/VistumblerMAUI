using System.Globalization;

namespace VistumblerMAUI.Localization;

/// <summary>
/// Shows a picker choice in the chosen language while the code keeps comparing and saving the English value
/// ("No grouping", "Cell towers"…). The text comes from the key "Opt_" + the value without spaces or punctuation,
/// and falls back to the value itself. Use as a Picker's ItemDisplayBinding:
/// <c>ItemDisplayBinding="{Binding ., Converter={StaticResource LocValue}}"</c>.
/// </summary>
public sealed class LocValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text.Length == 0) return value;
        var key = "Opt_" + new string(text.Where(char.IsLetterOrDigit).ToArray());
        var translated = Loc.T(key);
        return translated == key ? text : translated;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
