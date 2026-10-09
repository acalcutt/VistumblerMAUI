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
    Vsz,
    M8b,
    GeoJson,
    GeoJsonSignalMap,
    KmlSignalMap
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
    [ObservableProperty] private string _exportFolder = Services.SaveFolder.Describe(Services.ExportLocation.Resolve().Folder);

    /// <summary>Whether that folder was picked rather than the app's default, which is
    /// what the "Use default folder" button is offered for.</summary>
    [ObservableProperty] private bool _isCustomFolder = Services.ExportLocation.Resolve().UsedChoice;

    [ObservableProperty] private bool _includeOpenNetworks = true;
    [ObservableProperty] private bool _includeWepNetworks = true;
    [ObservableProperty] private bool _includeSecureNetworks = true;
    [ObservableProperty] private bool _useSignalColors = true;
    [ObservableProperty] private bool _includeGpsTrack = true;   // KML/GPX <trk>/LineString

    public List<ExportFormat> Formats { get; } = Enum.GetValues<ExportFormat>().ToList();

    private readonly ScanViewModel _scan;

    /// <summary>The original's "Filtered APs" exports: only the APs the filter in use (Filters page) shows.</summary>
    [ObservableProperty] private bool _filteredOnly;
    public bool HasActiveFilter => Services.ApFilterStore.Active is not null;
    public string FilteredOnlyText => $"Only APs the filter \"{Services.ApFilterStore.Active?.Name}\" shows";

    public ExportViewModel(IExportService exportService, IDatabaseService databaseService, ScanViewModel scan)
    {
        _exportService = exportService;
        _databaseService = databaseService;
        _scan = scan;
    }

    /// <summary>Return to Settings without exporting.</summary>
    [RelayCommand]
    private static Task CancelAsync() => Shell.Current.GoToAsync("..");

    /// <summary>
    /// Choose the folder to export into, through the platform's own picker.
    /// </summary>
    /// <remarks>
    /// On Android this is the system folder picker, and the export is written through the access it grants
    /// (SaveFolder): scoped storage doesn't let the app write to most shared folders by path, which is why a
    /// folder picked here used to be refused as not writable. Elsewhere the pick is checked for writability
    /// before it is kept, so a bad folder is refused here rather than at the end of the next export.
    /// </remarks>
    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        var (picked, error) = await Services.SaveFolder.PickAsync(Services.ExportLocation.Resolve().Folder);
        if (error is not null)
        {
            StatusMessage = $"{error} Keeping {ExportFolder}.";
            return;
        }
        if (picked is null) return;   // cancelled

        Services.ExportLocation.Chosen = picked;
        ExportFolder   = Services.SaveFolder.Describe(picked);
        IsCustomFolder = true;
        StatusMessage  = $"Exports will be written to {ExportFolder}";
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
            Services.ManufacturerDatabase.Current?.FillMissing(aps);   // APs saved before the lookup existed have none
            if (FilteredOnly && Services.ApFilterStore.Active is { } filter)
            {
                // Judged on the live list, so "active only" and signal rules mean what the list shows now
                await _scan.LoadCommand.ExecuteAsync(null);
                var shown = _scan.AllKnownAps.Where(filter.Matches).Select(a => a.Bssid).ToHashSet(StringComparer.OrdinalIgnoreCase);
                aps = aps.Where(a => shown.Contains(a.Bssid)).ToList();
                if (aps.Count == 0)
                {
                    StatusMessage = $"No access points match the filter \"{filter.Name}\"";
                    return;
                }
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
            ExportFolder   = Services.SaveFolder.Describe(folder);
            IsCustomFolder = usedChoice;

            var fileName = Path.GetFileNameWithoutExtension(name) + extension;

            // Saved through SaveFolder, since on Android a picked folder is written via its content URI
            var saved = await Services.SaveFolder.SaveAsync(folder, fileName, Write)
                        ?? throw new IOException("Nothing was written.");
            var where = Services.SaveFolder.Describe(saved);

            StatusMessage = fellBack
                ? $"Exported {aps.Count} access point(s) to {where} — the chosen folder could not be written to"
                : $"Exported {aps.Count} access point(s) to {where}";

            // Offer the file to whatever can take it off the device.
            //
            // On Android the default directory is app-private internal storage: no
            // file manager can see it and no browser can attach it, so an export
            // that "succeeded" left the data somewhere the person who asked for it
            // could not reach — which is the whole point of exporting. The share
            // sheet is the platform's answer, and it is also what makes uploading a
            // scan to WifiDB possible from the phone that recorded it.
            var (local, _) = await Services.SaveFolder.GetLocalFileAsync(saved);
            await ShareAsync(local);

            async Task Write(string path)
            {
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
                        // Cell towers and Bluetooth devices too, as WiGLE writes them; WifiDB imports both
                        await _exportService.ExportToWigleCsvAsync(path, aps, FilteredOnly ? null : await LoadRadiosAsync());
                        break;
                    case ExportFormat.Vs1:
                        await _exportService.ExportToVs1Async(path, aps, gpsFixes);
                        break;
                    case ExportFormat.Vsz:
                        await _exportService.ExportToVszAsync(path, aps, gpsFixes);
                        break;
                    case ExportFormat.GeoJson:
                        await Services.SignalMapExport.WriteGeoJsonAsync(path, aps, signalMap: false);
                        break;
                    case ExportFormat.GeoJsonSignalMap:
                        // Every reading, as WifiDB's signal map and WiGLE's site survey export
                        await Services.SignalMapExport.WriteGeoJsonAsync(path, aps, signalMap: true);
                        break;
                    case ExportFormat.KmlSignalMap:
                        await Services.SignalMapExport.WriteKmlSignalMapAsync(path, aps, Path.GetFileNameWithoutExtension(path));
                        break;
                    case ExportFormat.M8b:
                        // WiGLE's Magic 8 Ball: each AP's best-signal position, for offline "where am I"
                        await Task.Run(() =>
                        {
                            using var stream = File.Create(path);
                            Services.M8b.M8bFile.Write(stream, aps.Where(a => a.HasGps)
                                .Select(a => (a.Bssid, a.Latitude!.Value, a.Longitude!.Value)));
                        });
                        break;
                }
            }
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
                Title = "VistumblerMAUI export",
                File = new ShareFile(path)
            });
        }
        catch (Exception)
        {
            // The file is written; where it goes next is the platform's business.
        }
    }

    /// <summary>The session's cell towers and Bluetooth devices, with their readings.</summary>
    private async Task<List<RadioNetwork>> LoadRadiosAsync()
    {
        var radios = await _databaseService.GetAllRadioNetworksAsync();
        foreach (var n in radios) n.History = await _databaseService.GetRadioHistoryAsync(n.Id);
        return radios;
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
        ExportFormat.M8b => ".m8b",
        ExportFormat.GeoJson => ".geojson",
        ExportFormat.GeoJsonSignalMap => ".geojson",
        ExportFormat.KmlSignalMap => ".kml",
        _ => ".txt"
    };
}
