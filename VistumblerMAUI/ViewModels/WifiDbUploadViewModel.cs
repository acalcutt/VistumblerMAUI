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

    // The account comes from Settings → WifiDB. WifiDB needs a username to import (with the API key when the
    // account requires one), so without one the page points to Settings instead of offering the upload.
    public bool HasAccount => !string.IsNullOrWhiteSpace(WifiDbSettings.User);
    public bool NeedsAccount => !HasAccount;
    public string AccountText => HasAccount
        ? $"Uploading as {WifiDbSettings.User} to {WifiDbSettings.Url}" +
          (string.IsNullOrWhiteSpace(WifiDbSettings.ApiKey) ? " (no API key set)" : "")
        : "Set up your WifiDB account in Settings → WifiDB before uploading.";

    /// <summary>Re-reads the account, e.g. on returning from Settings.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(HasAccount));
        OnPropertyChanged(nameof(NeedsAccount));
        OnPropertyChanged(nameof(AccountText));
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
            StatusMessage = "Preparing the file…";
            int count = await SessionFileExporter.ExportAsync(_db, _export, path, format);
            if (count == 0)
            {
                StatusMessage = "There are no access points to upload.";
                return;
            }

            StatusMessage = $"Uploading {count} access points to WifiDB…";
            var result = await WifiDbUploader.UploadAsync(path, WifiDbSettings.User, WifiDbSettings.ApiKey,
                OtherUsers, Title, Notes);
            if (result.Success)
            {
                FileHash = result.FileHash;
                StatusMessage = $"{result.Message}\nImport #{result.ImportId} — {count} access points.";
            }
            else
            {
                StatusMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Upload failed: {ex.Message}";
        }
        finally
        {
            try { File.Delete(path); } catch { /* temporary copy */ }
            IsBusy = false;
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
