using VistumblerMAUI.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Services;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Upload to WifiDB (hamburger menu): writes the current session to a VS1/VSZ file and sends it to WifiDB's
/// import API with a title and notes, like the original Vistumbler's and VistumblerCS's upload windows.
/// WifiDB imports it later; Check Status follows it through the queue.
/// </summary>
public partial class WifiDbUploadViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly IExportService   _export;

    public IReadOnlyList<string> Formats { get; } = new[] { "VS1", "VSZ (zipped)" };

    [ObservableProperty] private string _otherUsers = string.Empty;
    [ObservableProperty] private string _title      = $"VistumblerMAUI {DateTime.Now:yyyy-MM-dd HH:mm}";
    [ObservableProperty] private string _notes      = string.Empty;
    [ObservableProperty] private string _selectedFormat = "VS1";
    [ObservableProperty] private string _statusMessage  = string.Empty;
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string? _fileHash;

    public bool CanCheckStatus => !string.IsNullOrEmpty(FileHash) && !IsBusy;

    // WiGLE, optional (Settings → WiGLE): offered only when it's turned on, and ticked when it has an account
    public bool ShowWigle => WigleSettings.Enabled;
    [ObservableProperty] private bool _alsoWigle = WigleSettings.Ready;
    public string WigleText => WigleSettings.HasAccount
        ? Loc.T("Upload_AlsoWigle")
        : Loc.T("Upload_AlsoWigleNoAccount");

    // The account comes from Settings → WifiDB. WifiDB needs a username to import (with the API key when the
    // account requires one), so without one the page points to Settings instead of offering the upload.
    public bool HasAccount => !string.IsNullOrWhiteSpace(WifiDbSettings.User);
    public bool NeedsAccount => !HasAccount;
    public string AccountText => HasAccount
        ? Loc.T("Upload_As", WifiDbSettings.User, WifiDbSettings.Url) +
          (string.IsNullOrWhiteSpace(WifiDbSettings.ApiKey) ? " " + Loc.T("Upload_NoApiKey") : "")
        : Loc.T("Upload_NeedAccount");

    /// <summary>Re-reads the account, e.g. on returning from Settings.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(HasAccount));
        OnPropertyChanged(nameof(NeedsAccount));
        OnPropertyChanged(nameof(AccountText));
        OnPropertyChanged(nameof(ShowWigle));
        OnPropertyChanged(nameof(WigleText));
        UploadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private static Task OpenWifiDbSettingsAsync() => Shell.Current.GoToAsync("//SettingsPage");

    public WifiDbUploadViewModel(IDatabaseService db, IExportService export)
    {
        _db     = db;
        _export = export;
    }

    partial void OnIsBusyChanged(bool value)
    {
        UploadCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCheckStatus));
    }

    partial void OnFileHashChanged(string? value) => OnPropertyChanged(nameof(CanCheckStatus));

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        IsBusy = true;
        FileHash = null;

        var format = SelectedFormat.StartsWith("VSZ") ? SaveFileFormat.Vsz : SaveFileFormat.Vs1;
        var path = Path.Combine(FileSystem.CacheDirectory,
            $"{DateTime.Now:yyyy-MM-dd HH-mm-ss}_WifiDB{SaveAndClearSettings.Extension(format)}");
        try
        {
            StatusMessage = Loc.T("Upload_Preparing");
            int count = await SessionFileExporter.ExportAsync(_db, _export, path, format);
            if (count == 0)
            {
                StatusMessage = Loc.T("Upload_NoAps");
                return;
            }

            StatusMessage = Loc.T("Upload_Uploading", count);
            var result = await WifiDbUploader.UploadAsync(path, WifiDbSettings.User, WifiDbSettings.ApiKey,
                OtherUsers, Title, Notes);
            if (result.Success)
            {
                FileHash = result.FileHash;
                StatusMessage = result.Message + "\n" + Loc.T("Upload_ImportNumber", result.ImportId, count);
            }
            else
            {
                StatusMessage = result.Message;
            }

            if (ShowWigle && AlsoWigle && WigleSettings.HasAccount)
                StatusMessage += "\n" + await UploadToWigleAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.T("Upload_Failed", ex.Message);
        }
        finally
        {
            try { File.Delete(path); } catch { /* temporary copy */ }
            IsBusy = false;
        }
    }

    /// <summary>The same session as a gzipped WiGLE CSV, with its cell towers and Bluetooth devices, sent to WiGLE.</summary>
    private async Task<string> UploadToWigleAsync()
    {
        var gz = Path.Combine(FileSystem.CacheDirectory, $"WigleWifi_{DateTime.Now:yyyyMMddHHmmss}.csv.gz");
        try
        {
            StatusMessage += "\n" + Loc.T("Upload_UploadingWigle");
            if (await WigleUploader.WriteSessionAsync(_db, _export, gz) == 0) return Loc.T("Upload_NothingForWigle");
            var result = await WigleUploader.UploadAsync(gz);
            StatusMessage = StatusMessage.Replace("\n" + Loc.T("Upload_UploadingWigle"), "");
            return result.Message;
        }
        finally
        {
            try { File.Delete(gz); } catch { /* temporary copy */ }
        }
    }

    private bool CanUpload() => !IsBusy && HasAccount;

    [RelayCommand]
    private async Task CheckStatusAsync()
    {
        if (string.IsNullOrEmpty(FileHash)) return;
        IsBusy = true;
        try { StatusMessage = await WifiDbUploader.CheckStatusAsync(FileHash); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private static Task CloseAsync() => Shell.Current.GoToAsync("..");
}
