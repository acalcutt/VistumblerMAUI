using VistumblerMAUI.Localization;
using Vistumbler.Core.Services;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.Views;

/// <summary>
/// Shows a newer release and its notes (pushed modally by <see cref="AppUpdater"/>). An installed Windows copy
/// downloads the matching setup.exe and runs it in update mode; Android opens the APK download; anything else is sent
/// to the release page.
/// </summary>
public partial class UpdatePage : ContentPage
{
    private readonly ReleaseInfo _release;
    private readonly HttpClient _http;
    private readonly ReleaseAsset? _asset;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _download;

    /// <summary>Completes when the page closes: true once the Windows installer is running and the app should exit.</summary>
    public Task<bool> Completion => _completion.Task;

    public UpdatePage(UpdateCheckResult result, HttpClient http, string? assetSuffix)
    {
        InitializeComponent();
        _release = result.Update!;
        _http = http;
        _asset = assetSuffix is null ? null : _release.FindAsset(assetSuffix);

        HeadingLabel.Text = Loc.T("Update_Available", AppUpdater.ProductName, _release.Version);
        VersionLabel.Text = Loc.T("Update_YouHave", result.CurrentVersion) +
                            (_release.Version.IsPrerelease ? " " + Loc.T("Update_Prerelease") : "");
        NotesLabel.Text = string.IsNullOrWhiteSpace(_release.Notes)
            ? Loc.T("Update_NoNotes")
            : _release.Notes.Trim();

        if (_asset is not null && OperatingSystem.IsWindows())
        {
            PrimaryButton.Text = Loc.T("Update_InstallRestart");
            InfoLabel.Text = Loc.T("Update_InstallInfo", AppUpdater.ProductName);
        }
        else if (_asset is not null && OperatingSystem.IsAndroid())
        {
            PrimaryButton.Text = Loc.T("Update_Download");
            InfoLabel.Text = Loc.T("Update_DownloadInfo");
        }
        else
        {
            PrimaryButton.Text = Loc.T("Update_OpenPage");
            ReleasePageButton.IsVisible = false;
            InfoLabel.Text = OperatingSystem.IsWindows() && !WindowsUpdateInstaller.IsInstalledCopy
                ? Loc.T("Update_NotInstalled")
                : Loc.T("Update_NoDownload");
        }
    }

    private async void OnPrimaryClicked(object? sender, EventArgs e)
    {
        if (_asset is not null && OperatingSystem.IsWindows())
        {
            await InstallOnWindowsAsync(_asset);
            return;
        }
        await OpenAsync(_asset?.Url ?? _release.PageUrl);
        await CloseAsync(installerStarted: false);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task InstallOnWindowsAsync(ReleaseAsset installer)
    {
        PrimaryButton.IsEnabled = false;
        ReleasePageButton.IsEnabled = false;
        ProgressPanel.IsVisible = true;
        StatusLabel.Text = Loc.T("Update_Downloading", installer.Name);
        _download = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p => DownloadProgress.Progress = p);
            var path = await WindowsUpdateInstaller.DownloadAsync(_http, installer, AppUpdater.ProductName, progress, _download.Token);

            StatusLabel.Text = Loc.T("Update_Checking");
            WindowsUpdateInstaller.VerifyPublisher(path);

            StatusLabel.Text = Loc.T("Update_Starting");
            if (!WindowsUpdateInstaller.Launch(path))
            {
                StatusLabel.Text = Loc.T("Update_Cancelled");
                PrimaryButton.IsEnabled = true;
                ReleasePageButton.IsEnabled = true;
                return;
            }
            await CloseAsync(installerStarted: true);
        }
        catch (OperationCanceledException) when (_download.IsCancellationRequested)
        {
            // Later was pressed during the download; the page is closing
        }
        catch (Exception ex)
        {
            StatusLabel.Text = Loc.T("Update_Failed", ex.Message);
            PrimaryButton.IsEnabled = true;
            ReleasePageButton.IsEnabled = true;
        }
        finally
        {
            _download?.Dispose();
            _download = null;
        }
    }

    private async void OnReleasePageClicked(object? sender, EventArgs e) => await OpenAsync(_release.PageUrl);

    private async void OnLaterClicked(object? sender, EventArgs e) => await CloseAsync(installerStarted: false);

    protected override void OnDisappearing()
    {
        // Also covers the Android back button
        base.OnDisappearing();
        _download?.Cancel();
        _completion.TrySetResult(false);
    }

    private async Task CloseAsync(bool installerStarted)
    {
        _download?.Cancel();
        _completion.TrySetResult(installerStarted);
        if (Navigation.ModalStack.Contains(this)) await Navigation.PopModalAsync();
    }

    private static async Task OpenAsync(string url)
    {
        if (!string.IsNullOrEmpty(url)) await Launcher.OpenAsync(new Uri(url));
    }
}
