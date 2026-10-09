using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Models;
using VistumblerMAUI.Services;
using VistumblerMAUI.Services.M8b;

namespace VistumblerMAUI.ViewModels;

/// <summary>An imported m8b file, as listed on the GPS details page.</summary>
public sealed record M8bFileItem(string Name, string Detail);

/// <summary>
/// Wi-Fi position on the GPS details page: an offline estimate of where you are from the APs in range, looked up
/// in imported Magic 8 Ball (m8b) files and the open session (<see cref="M8bLocator"/>). The m8b format is WiGLE
/// WiFi Wardriving's, so their exports work here too.
/// </summary>
public partial class GpsDetailsViewModel
{
    private int _wifiTicks;
    private IReadOnlyList<M8bMatch> _matches = Array.Empty<M8bMatch>();

    [ObservableProperty] private string _wifiPosition = "—";
    [ObservableProperty] private string _wifiPositionDetail = string.Empty;
    [ObservableProperty] private bool _hasWifiPosition;
    [ObservableProperty] private bool _useSessionAps = M8bLocator.UseSession;
    [ObservableProperty] private string _m8bStatus = string.Empty;

    public ObservableCollection<M8bFileItem> M8bFiles { get; } = new();

    partial void OnUseSessionApsChanged(bool value)
    {
        M8bLocator.UseSession = value;
        UpdateWifiPosition();
    }

    private void StartWifiPosition()
    {
        RefreshM8bFiles();
        UpdateWifiPosition();
    }

    // Called each second by the page timer; looks up every third second
    private void WifiPositionTick()
    {
        if (++_wifiTicks % 3 == 0) UpdateWifiPosition();
    }

    private void RefreshM8bFiles()
    {
        M8bFiles.Clear();
        foreach (var (name, records) in M8bLocator.Files())
            M8bFiles.Add(new M8bFileItem(name, $"{records:N0} records"));
    }

    private void UpdateWifiPosition()
    {
        var known = _scan.AllKnownAps;
        var visible = known.Where(a => a.IsActive).Select(a => a.Bssid).ToList();
        if (!M8bLocator.HasSources(known))
        {
            ShowMatches(Array.Empty<M8bMatch>(), "Nothing to look up in yet: import an m8b file, or scan with GPS first.");
            return;
        }
        if (visible.Count == 0)
        {
            ShowMatches(Array.Empty<M8bMatch>(), "No APs in range. Start scanning to estimate a position.");
            return;
        }

        IReadOnlyList<M8bMatch> matches;
        try { matches = M8bLocator.Locate(visible, known); }
        catch (Exception ex)
        {
            ShowMatches(Array.Empty<M8bMatch>(), $"Lookup failed: {ex.Message}");
            return;
        }
        if (matches.Count == 0)
        {
            ShowMatches(matches, $"None of the {visible.Count} APs in range are known.");
            return;
        }

        var best = matches[0];
        int tied = matches.Count(m => m.Count == best.Count);
        var detail = $"{best.Count} of {visible.Count} APs in range agree on 1 km square {best.Square}";
        if (tied > 1) detail += $" ({tied - 1} other square(s) tie)";
        if (_gps.CurrentGpsData is { } fix && (fix.Latitude != 0 || fix.Longitude != 0))
            detail += $"\nGPS fix is {Distance(fix.Latitude, fix.Longitude, best.Latitude, best.Longitude):N0} m from its centre";
        ShowMatches(matches, detail);
    }

    private void ShowMatches(IReadOnlyList<M8bMatch> matches, string detail)
    {
        _matches = matches;
        HasWifiPosition = matches.Count > 0;
        WifiPosition = matches.Count > 0 ? GpsFormatter.ToText(matches[0].Latitude, matches[0].Longitude) + " (± 1 km square)" : "—";
        WifiPositionDetail = detail;
    }

    [RelayCommand]
    private async Task ImportM8bAsync()
    {
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose an m8b file" });
            if (picked is null) return;
            M8bStatus = "Importing…";
            await using var stream = await picked.OpenReadAsync();
            int records = await M8bLocator.ImportAsync(stream, picked.FileName);
            M8bStatus = $"Imported {picked.FileName}: {records:N0} records";
            RefreshM8bFiles();
            UpdateWifiPosition();
        }
        catch (InvalidDataException ex) { M8bStatus = $"That isn't an m8b file this app can read ({ex.Message})"; }
        catch (Exception ex) { M8bStatus = $"Import failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task RemoveM8bAsync(M8bFileItem item)
    {
        if (!await Shell.Current.DisplayAlertAsync("Remove m8b file", $"Remove {item.Name} from the app?", "Remove", "Cancel"))
            return;
        M8bLocator.Remove(item.Name);
        M8bStatus = $"Removed {item.Name}";
        RefreshM8bFiles();
        UpdateWifiPosition();
    }

    /// <summary>Opens the map on the best squares, shaded by how many APs agree on each.</summary>
    [RelayCommand]
    private async Task ShowWifiPositionOnMapAsync()
    {
        if (_matches.Count == 0) return;
        int top = _matches[0].Count;
        // The leaders, and anything close behind them, up to 10 squares
        var squares = _matches.Where(m => m.Count * 2 >= top).Take(10).Select(m => $"{m.Square}:{m.Count}");
        await Shell.Current.GoToAsync($"//MapPage?m8b={Uri.EscapeDataString(string.Join(",", squares))}");
    }

    private static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180;
        double dp = p2 - p1, dl = (lon2 - lon1) * Math.PI / 180;
        double h = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
