using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Settings → WiGLE: optional uploads to WiGLE, secondary to WifiDB and off by default. Turning it on shows the
/// account fields and WiGLE's terms; turning it off hides them and stops uploads (the account is kept).
/// </summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private bool   _wigleEnabled        = WigleSettings.Enabled;
    [ObservableProperty] private string _wigleApiName        = WigleSettings.ApiName;
    [ObservableProperty] private string _wigleApiToken       = WigleSettings.ApiToken;
    [ObservableProperty] private bool   _wigleDonate         = WigleSettings.Donate;
    [ObservableProperty] private bool   _wigleUploadEachSave = WigleSettings.UploadEachSave;
    [ObservableProperty] private bool   _hasPendingWigle;
    [ObservableProperty] private string _pendingWigleText    = string.Empty;
    [ObservableProperty] private bool   _isRetryingWigle;

    public string WigleTerms =>
        "WiGLE (wigle.net) is a separate service with its own rules. Uploading sends this app's Wi-Fi, cell tower and " +
        "Bluetooth records to WiGLE under WiGLE's terms of use, which govern how they're stored, shown and shared; " +
        "read them before turning this on. WifiDB uploads are unaffected.";

    partial void OnWigleEnabledChanged(bool value)
    {
        WigleSettings.Enabled = value;
        RefreshPendingWigle();
    }
    partial void OnWigleApiNameChanged(string value)       => WigleSettings.ApiName = value;
    partial void OnWigleApiTokenChanged(string value)      => WigleSettings.ApiToken = value;
    partial void OnWigleDonateChanged(bool value)          => WigleSettings.Donate = value;
    partial void OnWigleUploadEachSaveChanged(bool value)  => WigleSettings.UploadEachSave = value;

    private static WigleUploadQueue? WigleQueue =>
        IPlatformApplication.Current?.Services.GetService<WigleUploadQueue>();

    private void RefreshPendingWigle()
    {
        var queue = WigleQueue;
        int count = queue?.Count ?? 0;
        HasPendingWigle = count > 0;
        PendingWigleText = count == 0 ? string.Empty
            : $"{count} save{(count == 1 ? "" : "s")} waiting to upload to WiGLE" +
              (queue!.LastError is { } error ? $": {error}" : "");
    }

    private void OnWigleQueueChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshPendingWigle);

    /// <summary>Follow the WiGLE queue while Settings is on screen.</summary>
    public void WatchWigleQueue(bool watch)
    {
        if (WigleQueue is not { } queue) return;
        queue.Changed -= OnWigleQueueChanged;
        if (watch) queue.Changed += OnWigleQueueChanged;
        RefreshPendingWigle();
    }

    [RelayCommand]
    private async Task RetryWigleAsync()
    {
        if (WigleQueue is not { } queue) return;
        IsRetryingWigle = true;
        try { await queue.ProcessAsync(waitForRunning: true); }
        finally
        {
            IsRetryingWigle = false;
            RefreshPendingWigle();
        }
    }

    [RelayCommand]
    private async Task ClearWigleAsync()
    {
        if (WigleQueue is not { } queue) return;
        if (!await Shell.Current.DisplayAlertAsync("Waiting WiGLE uploads",
                "Stop trying to upload these saves to WiGLE? The VS1 files in the Save & Clear folder are kept.",
                "Stop uploading", "Cancel"))
            return;
        queue.Clear();
        RefreshPendingWigle();
    }

    [RelayCommand]
    private static Task OpenWigleAccountAsync() => Browser.Default.OpenAsync(WigleSettings.AccountUrl, BrowserLaunchMode.SystemPreferred);

    [RelayCommand]
    private static Task OpenWigleTermsAsync() => Browser.Default.OpenAsync(WigleSettings.TermsUrl, BrowserLaunchMode.SystemPreferred);
}
