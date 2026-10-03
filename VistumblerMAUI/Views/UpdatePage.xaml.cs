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

        HeadingLabel.Text = $"{AppUpdater.ProductName} {_release.Version} is available";
        VersionLabel.Text = $"You have version {result.CurrentVersion}." +
                            (_release.Version.IsPrerelease ? " This update is a pre-release." : "");
        NotesLabel.Text = string.IsNullOrWhiteSpace(_release.Notes)
            ? "No release notes were published for this version."
            : _release.Notes.Trim();

        if (_asset is not null && OperatingSystem.IsWindows())
        {
            PrimaryButton.Text = "Install and Restart";
            InfoLabel.Text = $"{AppUpdater.ProductName} will close while the update installs, then start again. " +
                             "Your current session is kept, so you can resume it.";
        }
        else if (_asset is not null && OperatingSystem.IsAndroid())
        {
            PrimaryButton.Text = "Download Update";
            InfoLabel.Text = "The update downloads in your browser. Open it to install over this version; " +
                             "your sessions and settings are kept.";
        }
        else
        {
            PrimaryButton.Text = "Open Download Page";
            ReleasePageButton.IsVisible = false;
            InfoLabel.Text = OperatingSystem.IsWindows() && !WindowsUpdateInstaller.IsInstalledCopy
                ? "This copy wasn't installed with the setup program, so download the new version from the release page."
                : "This release has no download for your device yet. See the release page.";
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
        StatusLabel.Text = $"Downloading {installer.Name}...";
        _download = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p => DownloadProgress.Progress = p);
            var path = await WindowsUpdateInstaller.DownloadAsync(_http, installer, AppUpdater.ProductName, progress, _download.Token);

            StatusLabel.Text = "Checking the installer's signature...";
            WindowsUpdateInstaller.VerifyPublisher(path);

            StatusLabel.Text = "Starting the installer...";
            if (!WindowsUpdateInstaller.Launch(path))
            {
                StatusLabel.Text = "The update was cancelled.";
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
            StatusLabel.Text = $"The update failed: {ex.Message}";
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
