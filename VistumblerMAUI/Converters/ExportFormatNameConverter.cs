using System.Globalization;
using VistumblerMAUI.Localization;
using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Converters;

/// <summary>
/// The name an <see cref="ExportFormat"/> is shown by on the Export page ("Vistumbler Detailed CSV", as the original
/// called it, rather than "Csv"), from the ExportFormat_… strings so it follows the language.
/// </summary>
public class ExportFormatNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ExportFormat format ? Loc.T($"ExportFormat_{format}") : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
