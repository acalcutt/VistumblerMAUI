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
            var importedAps = await ParseAsync(_filePath, SelectedImportType);

            if (importedAps.Count > 0)
            {
                StatusMessage = $"Saving {importedAps.Count} access points…";
                ProgressValue = 0.5;
                await SaveAsync(importedAps);

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

    /// <summary>
    /// The original's Import Folder: imports every file of the chosen type directly in a folder (not its
    /// subfolders), one after another, like VistumblerCS's Import Folder.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportFolderAsync()
    {
        var (folder, error) = await Services.SaveFolder.PickAsync(string.Empty);
        if (error is not null) { StatusMessage = error; return; }
        if (folder is null) return;   // cancelled

        var extensions = GetExtensions(SelectedImportType);
        IReadOnlyList<(string Name, string Location)> files;
        try
        {
            files = Services.SaveFolder.ListFiles(folder)
                .Where(f => extensions.Contains(Path.GetExtension(f.Name), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't read the folder: {ex.Message}";
            return;
        }
        if (files.Count == 0)
        {
            StatusMessage = $"No {string.Join(" / ", extensions)} files in {Services.SaveFolder.Describe(folder)}";
            return;
        }

        IsImporting = true;
        int totalAps = 0, failed = 0;
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                var (name, location) = files[i];
                StatusMessage = $"Importing {i + 1} of {files.Count}: {name}";
                ProgressValue = (double)i / files.Count;
                string? copy = null;
                try
                {
                    // A file in a folder picked on Android is a content URI; parse a local copy of it
                    var (local, isCopy) = await Services.SaveFolder.GetLocalFileAsync(location);
                    if (isCopy) copy = local;
                    var aps = await ParseAsync(local, SelectedImportType);
                    if (aps.Count > 0) await SaveAsync(aps);
                    totalAps += aps.Count;
                }
                catch (Exception ex)
                {
                    failed++;
                    Services.DebugLog.Write($"[Import] {name} failed: {ex.Message}");
                }
                finally
                {
                    if (copy is not null) try { File.Delete(copy); } catch { /* cache */ }
                }
            }
            ProgressValue = 1;
            StatusMessage = $"Done — {files.Count} file(s), {totalAps} access point(s)" +
                            (failed > 0 ? $"; {failed} file(s) couldn't be read" : "");
        }
        finally
        {
            IsImporting = false;
        }
    }

    private async Task<List<AccessPoint>> ParseAsync(string path, ImportType type)
    {
        bool Ext(string ext) => Path.GetExtension(path).Equals(ext, StringComparison.OrdinalIgnoreCase);
        return type switch
        {
            ImportType.VistumblerFile => Ext(".vsz")
                ? await _importService.ImportFromVszAsync(path)
                : await _importService.ImportFromVs1Async(path),
            ImportType.Netstumbler => await _importService.ImportFromNs1Async(path),
            ImportType.VistumblerDetailedCsv => await _importService.ImportFromCsvAsync(path),
            ImportType.WigleCsv => await _importService.ImportFromCsvAsync(path),
            ImportType.WardriveAndroid => await _importService.ImportFromCsvAsync(path),
            ImportType.KismetFiles => Ext(".netxml")
                ? await _importService.ImportFromNetXmlAsync(path)
                : await _importService.ImportFromKismetDbAsync(path),
            _ => new List<AccessPoint>()
        };
    }

    private async Task SaveAsync(List<AccessPoint> aps)
    {
        await _databaseService.InitializeAsync();
        // Files from other tools (and older Vistumbler versions) often have no manufacturer
        Services.ManufacturerDatabase.Current?.FillMissing(aps);
        // Writes AP + HIST + GPS rows and (re)computes each AP's history links.
        await _databaseService.ImportAccessPointsAsync(aps);
    }

    private static string[] GetExtensions(ImportType type) => type switch
    {
        ImportType.VistumblerFile => new[] { ".vs1", ".vsz", ".txt" },
        ImportType.Netstumbler    => new[] { ".ns1" },
        ImportType.KismetFiles    => new[] { ".kismet", ".netxml" },
        _                         => new[] { ".csv" },
    };

    private bool CanImport() => !IsImporting;

    partial void OnIsImportingChanged(bool value)
    {
        ImportCommand.NotifyCanExecuteChanged();
        ImportFolderCommand.NotifyCanExecuteChanged();
    }

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
