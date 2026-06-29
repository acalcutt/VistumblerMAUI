using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.ViewModels;

public enum ExportFormat
{
    Kml,
    Gpx,
    Ns1,
    KismetDb,
    NetXml,
    Csv,
    WigleCsv,
    Vs1,
    Vsz
}

/// <summary>
/// Drives the Export page — pulls all access points from the database and writes
/// them out in the selected format via <see cref="IExportService"/>.
/// </summary>
public partial class ExportViewModel : ObservableObject
{
    private readonly IExportService _exportService;
    private readonly IDatabaseService _databaseService;

    [ObservableProperty] private ExportFormat _selectedFormat = ExportFormat.Kml;
    [ObservableProperty] private string _fileName = $"vistumbler_{DateTime.Now:yyyyMMdd_HHmmss}";
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _isExporting;

    [ObservableProperty] private bool _includeOpenNetworks = true;
    [ObservableProperty] private bool _includeWepNetworks = true;
    [ObservableProperty] private bool _includeSecureNetworks = true;
    [ObservableProperty] private bool _useSignalColors = true;

    public List<ExportFormat> Formats { get; } = Enum.GetValues<ExportFormat>().ToList();

    public ExportViewModel(IExportService exportService, IDatabaseService databaseService)
    {
        _exportService = exportService;
        _databaseService = databaseService;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        IsExporting = true;
        StatusMessage = "Exporting…";

        try
        {
            await _databaseService.InitializeAsync();
            var aps = await _databaseService.GetAllAccessPointsAsync();

            if (aps.Count == 0)
            {
                StatusMessage = "No access points to export";
                return;
            }

            var extension = GetExtension(SelectedFormat);
            var name = string.IsNullOrWhiteSpace(FileName) ? $"vistumbler_{DateTime.Now:yyyyMMdd_HHmmss}" : FileName;
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.GetFileNameWithoutExtension(name) + extension);

            switch (SelectedFormat)
            {
                case ExportFormat.Kml:
                    var options = new ExportOptions
                    {
                        IncludeOpenNetworks = IncludeOpenNetworks,
                        IncludeWepNetworks = IncludeWepNetworks,
                        IncludeSecureNetworks = IncludeSecureNetworks,
                        UseSignalColors = UseSignalColors
                    };
                    await _exportService.ExportToKmlAsync(path, aps, options);
                    break;
                case ExportFormat.Gpx:
                    await _exportService.ExportToGpxAsync(path, aps);
                    break;
                case ExportFormat.Ns1:
                    await _exportService.ExportToNs1Async(path, aps);
                    break;
                case ExportFormat.KismetDb:
                    await _exportService.ExportToKismetDbAsync(path, aps);
                    break;
                case ExportFormat.NetXml:
                    await _exportService.ExportToNetXmlAsync(path, aps);
                    break;
                case ExportFormat.Csv:
                    await _exportService.ExportToCsvAsync(path, aps);
                    break;
                case ExportFormat.WigleCsv:
                    await _exportService.ExportToWigleCsvAsync(path, aps);
                    break;
                case ExportFormat.Vs1:
                    await _exportService.ExportToVs1Async(path, aps);
                    break;
                case ExportFormat.Vsz:
                    await _exportService.ExportToVszAsync(path, aps);
                    break;
            }

            StatusMessage = $"Exported {aps.Count} access point(s) to {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private bool CanExport() => !IsExporting;

    partial void OnIsExportingChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();

    private static string GetExtension(ExportFormat format) => format switch
    {
        ExportFormat.Kml => ".kml",
        ExportFormat.Gpx => ".gpx",
        ExportFormat.Ns1 => ".ns1",
        ExportFormat.KismetDb => ".kismet",
        ExportFormat.NetXml => ".netxml",
        ExportFormat.Csv => ".csv",
        ExportFormat.WigleCsv => ".csv",
        ExportFormat.Vs1 => ".vs1",
        ExportFormat.Vsz => ".vsz",
        _ => ".txt"
    };
}
