using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using Vistumbler.Core.Models;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// The session's Save &amp; Clear record: each save (with the file's MD5, how WifiDB knows the upload) and where its
/// APs were, kept in the session database so the Scan page can show a running total, the Uploads page each
/// save's status, and the map the session's earlier APs after the list was cleared.
/// </summary>
public partial class ScanViewModel
{
    private int _saveCount;
    private int _savedApCount;

    /// <summary>"2 saves · 10,015 APs saved", or empty before the session's first save.</summary>
    [ObservableProperty] private string _savesSummary = string.Empty;

    /// <summary>Raised on the UI thread after a save is recorded (or the session changes).</summary>
    public event EventHandler? SavesChanged;

    /// <summary>
    /// Records a save just written to <paramref name="path"/> and the points of the APs it holds. Called by
    /// Save &amp; Clear, with _persistLock held, before the APs are cleared.
    /// </summary>
    private async Task RecordSaveAsync(string path, string fileName, int apCount)
    {
        try
        {
            // The list only changes on the UI thread (OnAccessPointsDetected), so read it there
            var points = await MainThread.InvokeOnMainThreadAsync(() => _apMap.Values
                .Where(ap => ap.Latitude is { } lat && ap.Longitude is { } lon && double.IsFinite(lat) && double.IsFinite(lon))
                .Select(ap => new SavedPoint
                {
                    Bssid = ap.Bssid, Ssid = ap.Ssid ?? "", Authentication = ap.AuthText, Encryption = ap.EncryptionText,
                    RadioType = ap.RadioType ?? "", Manufacturer = ap.Manufacturer ?? "", Channel = ap.Channel,
                    SecurityType = Services.ApFilter.SecurityType(ap),
                    Signal = ap.HighestSignal ?? ap.Signal, Rssi = ap.HighestRssi ?? ap.Rssi,
                    Latitude = ap.Latitude!.Value, Longitude = ap.Longitude!.Value, LastSeen = ap.LastSeen,
                })
                .ToList());

            var save = new SessionSave
            {
                SavedUtc = DateTime.UtcNow, FileName = fileName, Path = path, ApCount = apCount,
                Md5 = await Md5Async(path), ToWifiDb = Services.SaveAndClearSettings.UploadToWifiDb,
            };
            await _db.AddSessionSaveAsync(save, points);
            await MainThread.InvokeOnMainThreadAsync(() => SetSaveTotals(_saveCount + 1, _savedApCount + apCount));
        }
        catch (Exception ex)
        {
            // Only the record; the save itself went through
            Services.DebugLog.Write($"[SaveAndClear] couldn't record the save: {ex.Message}");
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private static Task OpenUploadsAsync() => Shell.Current.GoToAsync(nameof(Views.UploadsPage));

    /// <summary>The session's totals from its database, when the session is opened.</summary>
    private async Task LoadSavesAsync()
    {
        var saves = await _db.GetSessionSavesAsync();
        await MainThread.InvokeOnMainThreadAsync(() => SetSaveTotals(saves.Count, saves.Sum(s => s.ApCount)));
    }

    private void SetSaveTotals(int saves, int aps)
    {
        _saveCount = saves;
        _savedApCount = aps;
        SavesSummary = saves == 0 ? string.Empty : Localization.Loc.T("Scan_SavesSummary", saves, aps);
        SavesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The file's MD5 as WifiDB writes it (lower-case hex), or empty when it can't be read back.</summary>
    private static async Task<string> Md5Async(string path)
    {
        string? copy = null;
        try
        {
            var (local, isCopy) = await Services.SaveFolder.GetLocalFileAsync(path);
            if (isCopy) copy = local;
            await using var stream = File.OpenRead(local);
            return Convert.ToHexStringLower(await MD5.HashDataAsync(stream));
        }
        catch (Exception ex)
        {
            Services.DebugLog.Write($"[SaveAndClear] couldn't hash {path}: {ex.Message}");
            return string.Empty;
        }
        finally
        {
            if (copy is not null) try { File.Delete(copy); } catch { /* cache */ }
        }
    }
}
