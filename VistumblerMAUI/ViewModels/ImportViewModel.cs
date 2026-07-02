using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.ViewModels;

public enum ImportType
{
    VistumblerFile,        // .vs1 / .vsz
    VistumblerDetailedCsv,
    Netstumbler,           // .ns1
    KismetFiles,           // .kismet (KismetDB) or .netxml — auto-detected by extension
    WardriveAndroid,
    WigleCsv
}

/// <summary>
/// Drives the Import page — picks a file, parses it via <see cref="IImportService"/>,
/// and merges the resulting access points into the local database.
/// Ported from VistumblerCS's ImportViewModel, adapted to MAUI's FilePicker.
/// </summary>
public partial class ImportViewModel : ObservableObject
{
    private readonly IImportService _importService;
    private readonly IDatabaseService _databaseService;

    [ObservableProperty] private string _fileName = string.Empty;
    private string _filePath = string.Empty;

    [ObservableProperty] private ImportType _selectedImportType = ImportType.VistumblerFile;

    [ObservableProperty] private string _statusMessage = "Pick a file to import";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isImporting;

    public List<ImportType> ImportTypes { get; } = Enum.GetValues<ImportType>().ToList();

    public ImportViewModel(IImportService importService, IDatabaseService databaseService)
    {
        _importService = importService;
        _databaseService = databaseService;
    }

    /// <summary>Return to Settings without importing.</summary>
    [RelayCommand]
    private static Task CancelAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var fileTypes = GetFileTypesForType(SelectedImportType);
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a file to import",
            FileTypes = fileTypes
        });

        if (result != null)
        {
            _filePath = result.FullPath;
            FileName = result.FileName;
            StatusMessage = "Ready to import";
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
        {
            StatusMessage = "Please select a valid file.";
            return;
        }

        IsImporting = true;
        ProgressValue = 0;
        StatusMessage = "Parsing file…";

        try
        {
            List<AccessPoint> importedAps = SelectedImportType switch
            {
                ImportType.VistumblerFile => Path.GetExtension(_filePath).Equals(".vsz", StringComparison.OrdinalIgnoreCase)
                    ? await _importService.ImportFromVszAsync(_filePath)
                    : await _importService.ImportFromVs1Async(_filePath),
                ImportType.Netstumbler => await _importService.ImportFromNs1Async(_filePath),
                ImportType.VistumblerDetailedCsv => await _importService.ImportFromCsvAsync(_filePath),
                ImportType.WigleCsv => await _importService.ImportFromCsvAsync(_filePath),
                ImportType.WardriveAndroid => await _importService.ImportFromCsvAsync(_filePath),
                ImportType.KismetFiles => Path.GetExtension(_filePath).Equals(".netxml", StringComparison.OrdinalIgnoreCase)
                    ? await _importService.ImportFromNetXmlAsync(_filePath)
                    : await _importService.ImportFromKismetDbAsync(_filePath),
                _ => new List<AccessPoint>()
            };

            if (importedAps.Count > 0)
            {
                await _databaseService.InitializeAsync();

                StatusMessage = $"Saving {importedAps.Count} access points…";
                ProgressValue = 0.5;

                // Writes AP + HIST + GPS rows and (re)computes each AP's history links.
                await _databaseService.ImportAccessPointsAsync(importedAps);

                ProgressValue = 1;
                StatusMessage = $"Done — imported {importedAps.Count} access point(s)";
            }
            else
            {
                ProgressValue = 0;
                StatusMessage = "No access points found in file";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    private bool CanImport() => !IsImporting;

    partial void OnIsImportingChanged(bool value) => ImportCommand.NotifyCanExecuteChanged();

    private static FilePickerFileType GetFileTypesForType(ImportType type) => type switch
    {
        ImportType.VistumblerFile => new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.WinUI, new[] { ".vs1", ".vsz" } },
            { DevicePlatform.Android, new[] { "*/*" } },
            { DevicePlatform.iOS, new[] { "public.data" } },
        }),
        ImportType.Netstumbler => new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.WinUI, new[] { ".ns1", ".txt" } },
            { DevicePlatform.Android, new[] { "*/*" } },
            { DevicePlatform.iOS, new[] { "public.data" } },
        }),
        ImportType.KismetFiles => new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.WinUI, new[] { ".kismet", ".netxml" } },
            { DevicePlatform.Android, new[] { "*/*" } },
            { DevicePlatform.iOS, new[] { "public.data" } },
        }),
        _ => new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.WinUI, new[] { ".csv" } },
            { DevicePlatform.Android, new[] { "text/csv", "text/comma-separated-values" } },
            { DevicePlatform.iOS, new[] { "public.comma-separated-values-text" } },
        }),
    };
}
