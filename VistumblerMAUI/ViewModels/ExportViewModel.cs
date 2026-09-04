using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Models;
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

    /// <summary>The folder exports are written to, shown on the page.</summary>
    [ObservableProperty] private string _exportFolder = Services.ExportLocation.Resolve().Folder;

    /// <summary>Whether that folder was picked rather than the app's default, which is
    /// what the "Use default folder" button is offered for.</summary>
    [ObservableProperty] private bool _isCustomFolder = Services.ExportLocation.Resolve().UsedChoice;

    [ObservableProperty] private bool _includeOpenNetworks = true;
    [ObservableProperty] private bool _includeWepNetworks = true;
    [ObservableProperty] private bool _includeSecureNetworks = true;
    [ObservableProperty] private bool _useSignalColors = true;
    [ObservableProperty] private bool _includeGpsTrack = true;   // KML/GPX <trk>/LineString

    public List<ExportFormat> Formats { get; } = Enum.GetValues<ExportFormat>().ToList();

    public ExportViewModel(IExportService exportService, IDatabaseService databaseService)
    {
        _exportService = exportService;
        _databaseService = databaseService;
    }

    /// <summary>Return to Settings without exporting.</summary>
    [RelayCommand]
    private static Task CancelAsync() => Shell.Current.GoToAsync("..");

    /// <summary>
    /// Choose the folder to export into, through the platform's own picker.
    /// </summary>
    /// <remarks>
    /// The pick is checked for writability before it is kept, so a folder the app cannot
    /// write to is refused here — with the reason on screen — rather than at the end of
    /// the next export. That check matters most on Android, where the picker will happily
    /// return a Storage Access Framework tree whose reported path scoped storage does not
    /// let this app write to.
    /// </remarks>
    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        try
        {
            var result = await FolderPicker.Default.PickAsync(ExportFolder, CancellationToken.None);

            if (!result.IsSuccessful)
            {
                // Cancelling is the ordinary case and says nothing worth reporting.
                if (result.Exception is not null and not OperationCanceledException)
                    StatusMessage = $"Could not choose a folder: {result.Exception.Message}";
                return;
            }

            var picked = result.Folder.Path;

            if (!Services.ExportLocation.IsWritable(picked))
            {
                StatusMessage = $"Cannot write to {picked} — keeping {ExportFolder}";
                return;
            }

            Services.ExportLocation.Chosen = picked;
            ExportFolder   = picked;
            IsCustomFolder = true;
            StatusMessage  = $"Exports will be written to {picked}";
        }
        catch (Exception ex)
        {
            // Not every platform offers a folder picker, and a refused permission
            // arrives here too. The chosen folder is left alone.
            StatusMessage = $"Could not choose a folder: {ex.Message}";
        }
    }

    /// <summary>Go back to writing exports into the app's own documents folder.</summary>
    [RelayCommand]
    private void UseDefaultFolder()
    {
        Services.ExportLocation.Reset();
        ExportFolder   = Services.ExportLocation.DefaultFolder;
        IsCustomFolder = false;
        StatusMessage  = $"Exports will be written to {ExportFolder}";
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

            // Load each AP's full signal/GPS history + the GPS fixes. Every format that
            // records per-observation data (NS1, KismetDB, WiGLE, VS1/VSZ, GPS tracks in
            // KML/GPX) needs this — the same history the official Vistumbler exports.
            foreach (var ap in aps)
                ap.SignalHistory = await _databaseService.GetSignalHistoryAsync(ap.ApId);
            var gpsFixes = await _databaseService.GetAllGpsAsync();

            var extension = GetExtension(SelectedFormat);
            var name = string.IsNullOrWhiteSpace(FileName) ? $"vistumbler_{DateTime.Now:yyyyMMdd_HHmmss}" : FileName;
            // Resolve rather than trust: a folder chosen on this page may since have
            // been removed, unmounted, or had its permission revoked, and it proves
            // the folder writable before anything is exported into it.
            var (folder, usedChoice) = Services.ExportLocation.Resolve();
            var fellBack = Services.ExportLocation.Chosen.Length > 0 && !usedChoice;

            // Keep the page honest about where the file actually went.
            ExportFolder   = folder;
            IsCustomFolder = usedChoice;

            var path = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + extension);

            switch (SelectedFormat)
            {
                case ExportFormat.Kml:
                    var options = new ExportOptions
                    {
                        IncludeOpenNetworks = IncludeOpenNetworks,
                        IncludeWepNetworks = IncludeWepNetworks,
                        IncludeSecureNetworks = IncludeSecureNetworks,
                        UseSignalColors = UseSignalColors,
                        ShowTrack = IncludeGpsTrack
                    };
                    await _exportService.ExportToKmlAsync(path, aps, options, gpsFixes);
                    break;
                case ExportFormat.Gpx:
                    await _exportService.ExportToGpxAsync(path, aps,
                        IncludeGpsTrack ? gpsFixes : new List<GpsData>());
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
                    await _exportService.ExportToCsvAsync(path, aps, gpsFixes);
                    break;
                case ExportFormat.WigleCsv:
                    await _exportService.ExportToWigleCsvAsync(path, aps);
                    break;
                case ExportFormat.Vs1:
                    await _exportService.ExportToVs1Async(path, aps, gpsFixes);
                    break;
                case ExportFormat.Vsz:
                    await _exportService.ExportToVszAsync(path, aps, gpsFixes);
                    break;
            }

            StatusMessage = fellBack
                ? $"Exported {aps.Count} access point(s) to {path} — the chosen folder could not be written to"
                : $"Exported {aps.Count} access point(s) to {path}";

            // Offer the file to whatever can take it off the device.
            //
            // On Android the directory above is app-private internal storage: no
            // file manager can see it and no browser can attach it, so an export
            // that "succeeded" left the data somewhere the person who asked for it
            // could not reach — which is the whole point of exporting. The share
            // sheet is the platform's answer, and it is also what makes uploading a
            // scan to WifiDB possible from the phone that recorded it.
            await ShareAsync(path);
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

    /// <summary>
    /// Hands an exported file to the platform share sheet.
    /// </summary>
    /// <remarks>
    /// Never allowed to fail the export. The bytes are on disk by the time this
    /// runs, so a device with nothing to share to, or a user who dismisses the
    /// sheet, must not turn a successful write into "Export failed".
    /// </remarks>
    private static async Task ShareAsync(string path)
    {
        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Vistumbler export",
                File = new ShareFile(path)
            });
        }
        catch (Exception)
        {
            // The file is written; where it goes next is the platform's business.
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
