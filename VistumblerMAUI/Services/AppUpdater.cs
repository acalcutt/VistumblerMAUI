using System.Reflection;
using Vistumbler.Core.Services;
using VistumblerMAUI.Views;

namespace VistumblerMAUI.Services;

/// <summary>
/// Checks the release feeds (GitLab, then GitHub) for a newer VistumblerMAUI and offers it. Installed Windows copies
/// update in place with the setup.exe; Android opens the APK download, which installs over the app and keeps its
/// data. iOS has no published builds, so it doesn't check. Runs from the menu, from Settings → Updates, and at startup
/// unless turned off there.
/// </summary>
public sealed class AppUpdater
{
    public const string ProductName = "VistumblerMAUI";

    private const string AutoCheckKey = "update_auto_check";
    private const string PrereleaseKey = "update_include_prereleases";

    // Also downloads the Windows installer, so no short overall timeout; the feed check has its own
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly UpdateService _service =
        new(Http, ProductName, CurrentVersion, "techidiots-llc/VistumblerMAUI", "acalcutt/VistumblerMAUI");
    private bool _checking;
    private bool _startupCheckDone;

    public static bool AutoCheck
    {
        get => Preferences.Get(AutoCheckKey, true);
        set => Preferences.Set(AutoCheckKey, value);
    }

    /// <summary>Offer pre-releases (e.g. 0.5.0-rc.1). They're offered anyway when running a pre-release.</summary>
    public static bool IncludePrereleases
    {
        get => Preferences.Get(PrereleaseKey, false);
        set => Preferences.Set(PrereleaseKey, value);
    }

    /// <summary>ApplicationDisplayVersion, stamped into the assembly by the csproj.</summary>
    public static string CurrentVersion =>
        typeof(AppUpdater).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DisplayVersion")?.Value ?? "0.0.0";

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsAndroid();

    /// <summary>The release file this copy updates from, or null when it can only be sent to the release page.</summary>
    private static string? PlatformAsset
    {
        get
        {
            if (OperatingSystem.IsAndroid()) return "-android.apk";
            if (OperatingSystem.IsWindows() && WindowsUpdateInstaller.IsInstalledCopy) return WindowsUpdateInstaller.InstallerSuffix;
            return null;
        }
    }

    /// <summary>The once-per-launch startup check; does nothing when turned off in Settings.</summary>
    public async Task CheckOnStartupAsync(Func<Task> exitForUpdate)
    {
        if (_startupCheckDone || !AutoCheck) return;
        _startupCheckDone = true;
        await CheckAsync(interactive: false, exitForUpdate);
    }

    /// <param name="interactive">
    /// True when the user asked: report "up to date" and errors. False at startup: stay quiet unless there is an update.
    /// </param>
    /// <param name="exitForUpdate">Closes the app, keeping the session, once the Windows installer has started.</param>
    public async Task CheckAsync(bool interactive, Func<Task> exitForUpdate)
    {
        var shell = Shell.Current;
        if (_checking || shell is null) return;
        if (!IsSupported)
        {
            if (interactive) await shell.DisplayAlert("Check for Updates", "Update checks aren't available on this platform.", "OK");
            return;
        }

        _checking = true;
        try
        {
            var asset = PlatformAsset;
            var result = await _service.CheckAsync(IncludePrereleases, asset);
            if (result.Update is null)
            {
                if (interactive)
                    await shell.DisplayAlert("Check for Updates",
                        $"You're running the latest version of {ProductName} ({result.CurrentVersion}).", "OK");
                return;
            }

            var page = new UpdatePage(result, Http, asset);
            await shell.Navigation.PushModalAsync(page);
            if (await page.Completion) await exitForUpdate();
        }
        catch (Exception ex) when (interactive)
        {
            await shell.DisplayAlert("Check for Updates", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            // Startup check: offline or a feed outage shouldn't interrupt the user
            System.Diagnostics.Debug.WriteLine($"[AppUpdater] Update check failed: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }
    }
}
