using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using VistumblerMAUI.Localization;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.ViewModels;

/// <summary>One save of the session on the Uploads page, with what became of it.</summary>
public sealed class UploadRow
{
    public string  Title       { get; init; } = string.Empty;
    public string  Detail      { get; init; } = string.Empty;
    public string  Status      { get; init; } = string.Empty;
    public Color   StatusColor { get; init; } = Colors.Gray;
    /// <summary>The import on WifiDB, once it has one.</summary>
    public string? Url         { get; init; }
    public bool    HasUrl      => !string.IsNullOrEmpty(Url);
}

/// <summary>
/// The Uploads page: this session's Save &amp; Clear files and where each one is (waiting on the phone, in WifiDB's
/// queue, importing, imported or failed), matched to WifiDB by the file's MD5, plus the account's all-time totals
/// from WifiDB's profile API.
/// </summary>
public partial class UploadsViewModel : ObservableObject
{
    private static readonly Color Ok = Color.FromArgb("#2E7D32"), Busy = Color.FromArgb("#1565C0"),
                                  Waiting = Color.FromArgb("#EF6C00"), Failed = Color.FromArgb("#C62828");

    private readonly IDatabaseService _db;
    private readonly WifiDbUploadQueue _queue;
    private bool _running;

    public ObservableCollection<UploadRow> Saves { get; } = new();

    [ObservableProperty] private string _sessionSummary = string.Empty;
    [ObservableProperty] private string _accountTitle = string.Empty;
    [ObservableProperty] private string _accountTotals = string.Empty;
    [ObservableProperty] private string _accountStatus = string.Empty;
    [ObservableProperty] private string? _accountUrl;
    [ObservableProperty] private bool _isRefreshing;

    public bool HasAccount => !string.IsNullOrWhiteSpace(WifiDbSettings.User);
    public bool NeedsAccount => !HasAccount;

    public UploadsViewModel(IDatabaseService db, WifiDbUploadQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        // RefreshView sets IsRefreshing itself before it runs this, so a separate flag stops overlapping runs
        if (_running) return;
        _running = true;
        IsRefreshing = true;
        try
        {
            var saves = await _db.GetSessionSavesAsync();
            SessionSummary = saves.Count == 0
                ? Loc.T("Uploads_NoSaves")
                : Loc.T("Scan_SavesSummary", saves.Count, saves.Sum(s => s.ApCount));

            WifiDbProfileInfo? profile = null;
            OnPropertyChanged(nameof(HasAccount));
            OnPropertyChanged(nameof(NeedsAccount));
            if (HasAccount)
            {
                AccountStatus = Loc.T("Uploads_Checking");
                try
                {
                    profile = await WifiDbProfile.FetchAsync(WifiDbSettings.User, WifiDbSettings.ApiKey);
                    AccountTitle = Loc.T("Uploads_AccountTitle", profile.Username, WifiDbSettings.Url);
                    AccountTotals = Loc.T("Uploads_AccountTotals", profile.Files, profile.Aps, profile.NewAps, profile.Gps,
                                          profile.NewCells, profile.NewBluetooth) +
                                    (profile.Efficiency is { } eff ? "\n" + Loc.T("Uploads_Efficiency", eff) : "") +
                                    (profile.FirstImport is { } first && profile.LastImport is { } last
                                        ? "\n" + Loc.T("Uploads_ImportDates", first.ToLocalTime().ToString("d"), last.ToLocalTime().ToString("g"))
                                        : "");
                    AccountUrl = string.IsNullOrEmpty(profile.Url) ? null : profile.Url;
                    AccountStatus = string.Empty;
                }
                catch (Exception ex)
                {
                    AccountStatus = Loc.T("Uploads_ProfileFailed", ex.Message);
                }
            }

            Saves.Clear();
            foreach (var save in saves.AsEnumerable().Reverse())   // newest first
                Saves.Add(RowFor(save, profile));
        }
        finally
        {
            IsRefreshing = false;
            _running = false;
        }
    }

    private UploadRow RowFor(SessionSave save, WifiDbProfileInfo? profile)
    {
        var detail = Loc.T("Uploads_SaveDetail", save.SavedUtc.ToLocalTime().ToString("g"), save.ApCount);
        (string Status, Color Color, string? Url) state;
        var hash = save.Md5;

        if (_queue.Contains(save.Path))
            state = (Loc.T("Uploads_OnPhone", _queue.ErrorFor(save.Path) ?? Loc.T("Status_Queued")), Waiting, null);
        else if (!save.ToWifiDb)
            state = (Loc.T("Uploads_NotUploaded"), Colors.Gray, null);
        else if (profile is null || hash.Length == 0)
            state = (Loc.T("Uploads_Uploaded"), Busy, null);
        else if (profile.Imports.TryGetValue(hash, out var done))
            state = (Loc.T("Uploads_Imported", done.Id, done.Aps, done.Gps) +
                     (done.Efficiency is { } eff ? " · " + Loc.T("Uploads_Efficiency", eff) : ""), Ok, done.Url);
        else if (profile.Importing.TryGetValue(hash, out var importing))
            state = (importing.Progress is { } p ? Loc.T("Uploads_ImportingProgress", p) : Loc.T("Uploads_Importing"), Busy, null);
        else if (profile.Waiting.TryGetValue(hash, out _))
            state = (Loc.T("Uploads_InQueue"), Busy, null);
        else if (profile.Bad.TryGetValue(hash, out var bad))
            state = (Loc.T("Uploads_Failed", bad.Error ?? ""), Failed, null);
        else
            state = (Loc.T("Uploads_Uploaded"), Busy, null);   // older than the imports WifiDB listed

        return new UploadRow { Title = save.FileName, Detail = detail, Status = state.Status, StatusColor = state.Color, Url = state.Url };
    }

    [RelayCommand]
    private static Task OpenAsync(string? url) =>
        string.IsNullOrEmpty(url) ? Task.CompletedTask : Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);

    [RelayCommand]
    private static Task OpenSettingsAsync() => Shell.Current.GoToAsync("//SettingsPage");
}
