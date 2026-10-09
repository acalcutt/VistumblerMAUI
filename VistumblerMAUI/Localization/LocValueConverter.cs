using System.Globalization;

namespace VistumblerMAUI.Localization;

/// <summary>
/// Shows a picker choice in the chosen language while the code keeps comparing and saving the English value
/// ("No grouping", "Cell towers"…); see <see cref="Loc.Opt"/>. Use as a Picker's ItemDisplayBinding:
/// <c>ItemDisplayBinding="{Binding ., Converter={StaticResource LocValue}, x:DataType=x:String}"</c>.
/// </summary>
public sealed class LocValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? Loc.Opt(text) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
