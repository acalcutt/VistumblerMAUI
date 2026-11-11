using VistumblerMAUI.Localization;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Graphics.Platform;
using Vistumbler.Core.Models;
using VistumblerMAUI.Controls;
using VistumblerMAUI.Services.Survey;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// A choice in the survey's network picker: everything, one SSID, or one AP; or, with <paramref name="HeardHere"/>,
/// the live list of what's heard where you're standing (the plan then shows everything, as for the strongest).
/// </summary>
public sealed record SurveyNetwork(string Label, string? Ssid, string? Bssid, bool HeardHere = false)
{
    public override string ToString() => Label;
}

/// <summary>An AP in the "heard here now" list: its name, BSSID, latest RSSI, and channel and security.</summary>
public sealed record HeardAp(string Name, string Bssid, int Rssi, string Detail);

/// <summary>A choice in the Survey menu.</summary>
public sealed record SurveyMenuItem(string Label, IAsyncRelayCommand Command);

/// <summary>
/// Site survey (tap-to-mark), for indoors where GPS can't place readings: stand somewhere, tap that spot on the
/// floor plan, and the next scan's readings are kept there. Marks are drawn coloured by the chosen network's RSSI,
/// with a heatmap between them. The plan is an image (a floor plan, a photo of the fire-escape map) or a grid.
/// </summary>
public partial class SurveyViewModel : ObservableObject
{
    private readonly ScanViewModel _scan;
    private Survey _survey = new();

    public SurveyDrawable Drawable { get; } = new();

    /// <summary>Raised when the drawing needs repainting; <c>true</c> when the plan changed and should be refitted.</summary>
    public event Action<bool>? Redraw;

    [ObservableProperty] private string _title = Loc.T("Menu_SiteSurvey");
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _showHeatmap = true;
    [ObservableProperty] private SurveyNetwork? _selectedNetwork;
    [ObservableProperty] private bool _isWaiting;
    [ObservableProperty] private bool _isScanning;

    public ObservableCollection<SurveyNetwork> Networks { get; } = new();

    private static readonly SurveyNetwork Strongest = new(Loc.T("Survey_Strongest"), null, null);
    private static readonly SurveyNetwork HeardHereNow = new(Loc.T("Survey_HeardHere"), null, null, HeardHere: true);

    /// <summary>What the plan's colours show: the chosen network, or the strongest while the live list is chosen.</summary>
    private SurveyNetwork MapNetwork => SelectedNetwork is { HeardHere: false } n ? n : Strongest;

    /// <summary>While "All APs heard here now" is chosen: every AP the scan hears now, strongest first.</summary>
    public ObservableCollection<HeardAp> HeardHere { get; } = new();

    [ObservableProperty] private bool _showHeardHere;

    /// <summary>The live list open (or folded to its one-line header, to leave the plan its room). Remembered.</summary>
    [ObservableProperty] private bool _heardHereExpanded = Preferences.Get(HeardHereExpandedKey, true);

    private const string HeardHereExpandedKey = "Survey_HeardHereExpanded";

    partial void OnHeardHereExpandedChanged(bool value) => Preferences.Set(HeardHereExpandedKey, value);

    [RelayCommand]
    private void ToggleHeardHere() => HeardHereExpanded = !HeardHereExpanded;

    public SurveyViewModel(ScanViewModel scan) => _scan = scan;

    public void Start()
    {
        _lastHeard = [];   // until this visit's first scan
        _scan.ScanCycleMerged += OnScanCycle;
        _scan.PropertyChanged += OnScanPropertyChanged;
        IsScanning = _scan.IsScanning;
        if (_survey.Marks.Count == 0 && _survey.PlanImage is null)
            Open(SurveyStore.Load(SurveyStore.LastId) ?? NewSurveyObject());
        else
        {
            RebuildNetworks();   // what the scan hears now
            UpdateStatus();
        }
    }

    public void Stop()
    {
        _scan.ScanCycleMerged -= OnScanCycle;
        _scan.PropertyChanged -= OnScanPropertyChanged;
    }

    private void OnScanPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanViewModel.IsScanning))
        {
            IsScanning = _scan.IsScanning;
            UpdateStatus();
        }
    }

    private static Survey NewSurveyObject() =>
        new() { Name = $"Survey {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)}" };

    private void Open(Survey survey)
    {
        _survey = survey;
        SurveyStore.LastId = survey.Id;
        Title = survey.Name;
        _pending = null;
        Drawable.Pending = null;
        IsWaiting = false;
        IsPlacingPlan = false;
        LoadPlanImage();
        RebuildNetworks();
        Refresh(refit: true);
        IsMapView = survey.OnMap;
        PlanChanged?.Invoke();
    }

    private void LoadPlanImage()
    {
        Drawable.PlanImage = null;
        Drawable.PlanWidth = _survey.PlanWidth;
        Drawable.PlanHeight = _survey.PlanHeight;
        if (_survey.PlanImagePath is not { } path || !File.Exists(path)) return;
        try
        {
            using var stream = File.OpenRead(path);
            var image = PlatformImage.FromStream(stream);
            // Big photos are drawn from a smaller copy; marks stay in the original's pixels
            Drawable.PlanImage = Math.Max(image.Width, image.Height) > 2400 ? image.Downsize(2400, disposeOriginal: true) : image;
        }
        catch (Exception ex)
        {
            Status = Loc.T("Survey_PlanLoadFailed", ex.Message);
        }
    }

    // ── Marking ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A tap on the plan, in plan units. On a spot already measured, shows it and offers to delete or re-measure
    /// it; elsewhere, waits for the next scan and keeps its readings there.
    /// </summary>
    public async Task TapAsync(double x, double y)
    {
        if (x < 0 || y < 0 || x > _survey.PlanWidth || y > _survey.PlanHeight) return;

        int hit = SpotNear(x, y);
        if (hit >= 0)
        {
            if (await SpotActionAsync(hit) is { } again) Measure(again);
            return;
        }
        Measure(new PendingSpot(x, y, null, null));
    }

    /// <summary>
    /// A tap on the map view, at a map position. <paramref name="hit"/> is the measured spot drawn there, or -1.
    /// </summary>
    public async Task MapTapAsync(double lat, double lon, int hit)
    {
        if (IsPlacingPlan) return;
        if (hit >= 0 && hit < _survey.Marks.Count)
        {
            if (await SpotActionAsync(hit) is { } again) Measure(again);
            return;
        }
        Measure(new PendingSpot(null, null, lat, lon));
    }

    /// <summary>
    /// A tap on a measured spot: shows it and offers to delete it or measure again there. Deletes it either way
    /// but cancel; for measure again, returns where it was, to measure there.
    /// </summary>
    private async Task<PendingSpot?> SpotActionAsync(int hit)
    {
        var mark = _survey.Marks[hit];
        string delete = Loc.T("Survey_DeleteSpot"), again = Loc.T("Survey_MeasureAgain");
        var choice = await Shell.Current.DisplayActionSheetAsync(Describe(hit), Loc.T("Common_Cancel"), delete, again);
        if (choice != delete && choice != again) return null;
        _survey.Marks.Remove(mark);
        Save();
        RebuildNetworks();
        Refresh(refit: false);
        if (choice == delete) return null;
        return mark.OnPlan ? new PendingSpot(mark.X, mark.Y, null, null) : new PendingSpot(null, null, mark.Latitude, mark.Longitude);
    }

    // The measured spot within a fingertip of a point on the plan, or -1
    private int SpotNear(double x, double y)
    {
        double reach = 20 / Drawable.Scale;
        int hit = -1;
        double best = reach * reach;
        for (int i = 0; i < _survey.Marks.Count; i++)
        {
            if (PlanOf(_survey.Marks[i]) is not { } m) continue;
            double d2 = (m.X - x) * (m.X - x) + (m.Y - y) * (m.Y - y);
            if (d2 <= best) { best = d2; hit = i; }
        }
        return hit;
    }

    private string Describe(int index)
    {
        var mark = _survey.Marks[index];
        var lines = new List<string> { Loc.T("Survey_SpotInfo", index + 1, mark.Readings.Count, mark.Time.ToLocalTime().ToString("t")) };
        lines.Add(ValueAt(mark) is { } v ? $"{MapNetwork.Label}: {v:0} dBm" : Loc.T("Survey_NotHeard", MapNetwork.Label));
        if (mark.Readings.OrderByDescending(r => r.Rssi).FirstOrDefault() is { } top)
            lines.Add(Loc.T("Survey_StrongestAp", string.IsNullOrEmpty(top.Ssid) ? top.Bssid : top.Ssid, top.Rssi));
        if (GeoOf(mark) is { } geo)
            lines.Add("⌖ " + GpsFormatter.ToText(geo.Lat, geo.Lon));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Where the next scan's readings go: a plan position (tapped on the plan) or a map one.</summary>
    private sealed record PendingSpot(double? X, double? Y, double? Lat, double? Lon);

    private PendingSpot? _pending;

    private void Measure(PendingSpot spot)
    {
        _pending = spot;
        IsWaiting = true;
        ShowPending();
        UpdateStatus();
    }

    // Draws the pending spot on the plan, where it has a plan position (or the plan is on the map)
    private void ShowPending()
    {
        Drawable.Pending = _pending switch
        {
            { X: { } x, Y: { } y } => (x, y),
            { Lat: { } lat, Lon: { } lon } when _survey.Corners is { } c => c.ToPlan(_survey.PlanWidth, _survey.PlanHeight, lat, lon),
            _ => null,
        };
        Redraw?.Invoke(false);
    }

    private void OnScanCycle(object? sender, IReadOnlyList<AccessPoint> heard)
    {
        // The live list is this scan, which is just what a spot tapped now records (every AP it heard)
        if (heard.Count > 0) _lastHeard = heard;
        RefreshHeardHere();
        if (_pending is not { } at || heard.Count == 0)
        {
            RebuildNetworks();   // APs the scan now hears join the network picker
            return;
        }
        _survey.Marks.Add(new SurveyMark
        {
            X = at.X ?? 0,
            Y = at.Y ?? 0,
            OnPlan = at.X is not null,
            Latitude = at.X is null ? at.Lat : null,
            Longitude = at.X is null ? at.Lon : null,
            Time = DateTime.UtcNow,
            Readings = heard.Select(ap => new SurveyReading
            {
                Bssid = ap.Bssid,
                Ssid = ap.Ssid,
                Rssi = ap.Rssi ?? SignalToRssi(ap.Signal ?? 0),
                Signal = ap.Signal ?? 0,
                Channel = ap.Channel,
                FrequencyMhz = ap.FrequencyMhz,
                Auth = ap.AuthText,
                Encryption = ap.EncryptionText,
            }).ToList(),
        });
        _pending = null;
        Drawable.Pending = null;
        IsWaiting = false;
        Save();
        RebuildNetworks();
        Refresh(refit: false);
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { /* not on every device */ }
    }

    // Windows reports signal % only; the usual mapping (0 % = -100 dBm, 100 % = -50 dBm)
    private static int SignalToRssi(int signal) => signal / 2 - 100;

    [RelayCommand]
    private void CancelMark()
    {
        _pending = null;
        Drawable.Pending = null;
        IsWaiting = false;
        UpdateStatus();
        Redraw?.Invoke(false);
    }

    [RelayCommand]
    private void UndoMark()
    {
        if (_survey.Marks.Count == 0) return;
        _survey.Marks.RemoveAt(_survey.Marks.Count - 1);
        Save();
        RebuildNetworks();
        Refresh(refit: false);
    }

    [RelayCommand]
    private Task StartScanningAsync() => _scan.IsScanning ? Task.CompletedTask : _scan.ToggleScanCommand.ExecuteAsync(null);

    // ── What's drawn ──────────────────────────────────────────────────────────

    partial void OnShowHeatmapChanged(bool value)
    {
        Drawable.ShowHeatmap = value;
        Redraw?.Invoke(false);
    }

    partial void OnSelectedNetworkChanged(SurveyNetwork? value)
    {
        ShowHeardHere = value?.HeardHere == true;
        RefreshHeardHere();
        Refresh(refit: false);
    }

    // The APs the latest scan heard, with that scan's signal: what a spot tapped now would record
    private IReadOnlyList<AccessPoint> _lastHeard = [];

    /// <summary>
    /// Fills the "heard here now" list, strongest first: the latest scan's APs, as a spot tapped now records them
    /// (every one of them goes in the survey and its CSV). Before this page's first scan, the list's active APs.
    /// UI thread.
    /// </summary>
    private void RefreshHeardHere()
    {
        HeardHere.Clear();
        if (!ShowHeardHere) return;
        var source = _lastHeard.Count > 0 ? _lastHeard : _scan.AllKnownAps.Where(a => a.IsActive);
        var heard = source
            .Select(a => new HeardAp(
                string.IsNullOrEmpty(a.Ssid) ? Loc.T("Group_Hidden") : a.Ssid,
                a.Bssid,
                a.Rssi ?? SignalToRssi(a.Signal ?? 0),
                $"{Loc.T("Scan_Channel", a.Channel)} · {a.AuthText}"))
            .OrderByDescending(a => a.Rssi)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var ap in heard) HeardHere.Add(ap);
    }

    /// <summary>A tap on an AP in the "heard here now" list: follow that AP on the plan.</summary>
    [RelayCommand]
    private void FollowAp(HeardAp ap) =>
        SelectedNetwork = Networks.FirstOrDefault(n => string.Equals(n.Bssid, ap.Bssid, StringComparison.OrdinalIgnoreCase)) ?? SelectedNetwork;

    /// <summary>
    /// The network picker's choices: the strongest at each spot, each SSID, then each AP. The SSIDs and APs are
    /// those heard at the measured spots and those the scan hears now (active in the last 30 seconds), so one AP
    /// can be chosen before the first spot is measured. Every spot keeps every AP it heard, so the choice only
    /// changes what's drawn. Only rebuilt when the choices change, as it's called after every scan.
    /// </summary>
    private void RebuildNetworks()
    {
        var keep = SelectedNetwork;
        var heard = _survey.Marks.SelectMany(m => m.Readings.Select(r => (r.Bssid, r.Ssid)))
            .Concat(_scan.AllKnownAps.Where(a => a.IsActive).Select(a => (a.Bssid, a.Ssid)))
            .DistinctBy(a => a.Bssid, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // How many spots heard each AP, so the most measured come first
        var spots = _survey.Marks.SelectMany(m => m.Readings.Select(r => r.Bssid).Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        int SpotsFor(string bssid) => spots.GetValueOrDefault(bssid);

        var options = new List<SurveyNetwork> { Strongest, HeardHereNow };
        options.AddRange(heard.Where(a => !string.IsNullOrEmpty(a.Ssid))
            .GroupBy(a => a.Ssid)
            .OrderByDescending(g => g.Max(a => SpotsFor(a.Bssid)))
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new SurveyNetwork(Loc.T("Survey_SsidAps", g.Key, g.Count()), g.Key, null)));
        options.AddRange(heard
            .OrderByDescending(a => SpotsFor(a.Bssid))
            .ThenBy(a => string.IsNullOrEmpty(a.Ssid))
            .ThenBy(a => a.Ssid, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Bssid, StringComparer.OrdinalIgnoreCase)
            .Select(a => new SurveyNetwork($"{(string.IsNullOrEmpty(a.Ssid) ? Loc.T("Group_Hidden") : a.Ssid)} · {a.Bssid}", null, a.Bssid)));

        // The chosen one stays, even once it's no longer heard
        var selected = keep is null ? null : options.FirstOrDefault(o => SameNetwork(o, keep));
        if (selected is null && keep is not null && keep != Strongest && keep != HeardHereNow)
        {
            options.Add(keep);
            selected = keep;
        }

        if (!Networks.Select(n => n.Label).SequenceEqual(options.Select(o => o.Label)))
        {
            Networks.Clear();
            foreach (var o in options) Networks.Add(o);
        }
        SelectedNetwork = Networks.FirstOrDefault(o => selected is not null && SameNetwork(o, selected)) ?? Strongest;
    }

    private static bool SameNetwork(SurveyNetwork a, SurveyNetwork b) =>
        string.Equals(a.Bssid, b.Bssid, StringComparison.OrdinalIgnoreCase) && a.Ssid == b.Ssid && a.HeardHere == b.HeardHere;

    /// <summary>The chosen network's RSSI at a mark: its strongest matching reading, or null if none heard it.</summary>
    private double? ValueAt(SurveyMark mark)
    {
        var n = MapNetwork;
        var matching = mark.Readings.Where(r =>
            n.Bssid is not null ? string.Equals(r.Bssid, n.Bssid, StringComparison.OrdinalIgnoreCase)
            : n.Ssid is null || r.Ssid == n.Ssid);
        return matching.Any() ? matching.Max(r => r.Rssi) : null;
    }

    private void Refresh(bool refit)
    {
        Drawable.Marks = _survey.Marks
            .Select(m => (Plan: PlanOf(m), Rssi: ValueAt(m)))
            .Where(m => m.Plan is not null)
            .Select(m => (m.Plan!.Value.X, m.Plan!.Value.Y, m.Rssi))
            .ToList();
        Drawable.RebuildHeatmap();
        UpdateStatus();
        Redraw?.Invoke(refit);
    }

    private void UpdateStatus()
    {
        if (IsPlacingPlan)
            Status = Loc.T("Survey_PlaceHint");
        else if (IsWaiting)
            Status = _scan.IsScanning ? Loc.T("Survey_Measuring") : Loc.T("Survey_StartToMeasure");
        else if (!_scan.IsScanning)
            Status = Loc.T("Survey_StartThenTap");
        else
            Status = _survey.Marks.Count == 0
                ? Loc.T("Survey_TapToMeasure")
                : Loc.T("Survey_SpotsMeasured", _survey.Marks.Count);
    }

    private void Save()
    {
        try { SurveyStore.Save(_survey); }
        catch (Exception ex) { Status = Loc.T("Survey_SaveFailed", ex.Message); }
    }

    // ── Surveys and plans ─────────────────────────────────────────────────────

    /// <summary>The Survey menu, which opens upward from its button at the start of the buttons under the plan.</summary>
    [ObservableProperty] private bool _isMenuOpen;

    private IReadOnlyList<SurveyMenuItem>? _menuItems;

    /// <summary>The Survey menu's choices.</summary>
    public IReadOnlyList<SurveyMenuItem> MenuItems => _menuItems ??=
    [
        MenuItem("Survey_New",       () => { Open(NewSurveyObject()); return Task.CompletedTask; }),
        MenuItem("Survey_Open",      OpenSurveyAsync),
        MenuItem("Survey_FloorPlan", PickPlanAsync),
        MenuItem("Survey_LineUpPlan", () => { StartPlacing(); return Task.CompletedTask; }),
        MenuItem("Survey_UseGrid",   () => { UseGrid(); return Task.CompletedTask; }),
        MenuItem("Survey_Rename",    RenameAsync),
        MenuItem("Survey_ExportCsv", ExportCsvAsync),
        MenuItem("Survey_Delete",    DeleteAsync),
    ];

    // Each choice closes the menu before it runs, so its dialogs aren't under the menu
    private SurveyMenuItem MenuItem(string key, Func<Task> run) =>
        new(Loc.T(key), new AsyncRelayCommand(async () => { IsMenuOpen = false; await run(); }));

    [RelayCommand]
    private void ToggleMenu() => IsMenuOpen = !IsMenuOpen;

    [RelayCommand]
    private void CloseMenu() => IsMenuOpen = false;

    private async Task OpenSurveyAsync()
    {
        var surveys = SurveyStore.List();
        if (surveys.Count == 0) { Status = Loc.T("Survey_NoneSaved"); return; }
        var labels = surveys.Select(s => Loc.T("Survey_NameSpots", s.Name, s.Marks.Count)).ToArray();
        var pick = await Shell.Current.DisplayActionSheetAsync(Loc.T("Survey_Open"), Loc.T("Common_Cancel"), null, labels);
        int i = Array.IndexOf(labels, pick);
        if (i >= 0) Open(surveys[i]);
    }

    private async Task PickPlanAsync()
    {
        if (_survey.Marks.Count > 0 &&
            !await Shell.Current.DisplayAlertAsync(Loc.T("Survey_FloorPlanTitle"),
                Loc.T("Survey_ChangePlanQuestion"), Loc.T("Survey_Change"), Loc.T("Common_Cancel")))
            return;
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Loc.T("Survey_ChoosePlan"), FileTypes = FilePickerFileType.Images });
            if (picked is null) return;
            await using (var stream = await picked.OpenReadAsync())
                await SurveyStore.SetPlanImageAsync(_survey, stream, picked.FileName);

            using (var check = File.OpenRead(_survey.PlanImagePath!))
            {
                var image = PlatformImage.FromStream(check);
                _survey.PlanWidth = image.Width;
                _survey.PlanHeight = image.Height;
                image.Dispose();
            }
            _survey.Corners = null;   // a new image isn't lined up on the map yet
            Save();
            LoadPlanImage();
            Refresh(refit: true);
            PlanChanged?.Invoke();
            if (IsMapView) StartPlacing();   // over the map: line it up straight away
        }
        catch (Exception ex)
        {
            Status = Loc.T("Survey_ImageFailed", ex.Message);
        }
    }

    private void UseGrid()
    {
        if (_survey.PlanImagePath is { } old) try { File.Delete(old); } catch (IOException) { }
        _survey.PlanImage = null;
        _survey.PlanWidth = _survey.PlanHeight = Survey.GridSize;
        _survey.Corners = null;
        IsPlacingPlan = false;
        Save();
        LoadPlanImage();
        Refresh(refit: true);
        PlanChanged?.Invoke();
    }

    private async Task RenameAsync()
    {
        var name = await Shell.Current.DisplayPromptAsync(Loc.T("Survey_RenameTitle"), Loc.T("Survey_Name"), Loc.T("Common_Ok"), Loc.T("Common_Cancel"), initialValue: _survey.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        _survey.Name = Title = name.Trim();
        Save();
    }

    private async Task DeleteAsync()
    {
        if (!await Shell.Current.DisplayAlertAsync(Loc.T("Survey_DeleteTitle"), Loc.T("Survey_DeleteQuestion", _survey.Name, _survey.Marks.Count), Loc.T("Common_Delete"), Loc.T("Common_Cancel")))
            return;
        SurveyStore.Delete(_survey);
        Open(SurveyStore.List().FirstOrDefault() ?? NewSurveyObject());
    }

    // ── Export ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every reading at every spot, one row each, with the spot's plan position and, once known, its map position
    /// (last, so the earlier columns stay where they were). Either can be blank: a spot tapped on the map has no
    /// plan position until the plan is lined up on the map, and one tapped on the plan has no map position until then.
    /// </summary>
    private async Task ExportCsvAsync()
    {
        if (_survey.Marks.Count == 0) { Status = Loc.T("Survey_NothingMeasured"); return; }
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("Spot,X,Y,Time (UTC),BSSID,SSID,RSSI,Signal,Channel,Frequency,Authentication,Encryption,Latitude,Longitude\n");
        for (int i = 0; i < _survey.Marks.Count; i++)
        {
            var m = _survey.Marks[i];
            var plan = PlanOf(m);
            var geo = GeoOf(m);
            string x = plan?.X.ToString("0.#", inv) ?? "", y = plan?.Y.ToString("0.#", inv) ?? "";
            string lat = geo?.Lat.ToString("0.0000000", inv) ?? "", lon = geo?.Lon.ToString("0.0000000", inv) ?? "";
            foreach (var r in m.Readings)
                sb.Append(i + 1).Append(',').Append(x).Append(',').Append(y).Append(',')
                  .Append(m.Time.ToString("yyyy-MM-dd HH:mm:ss", inv)).Append(',').Append(r.Bssid).Append(',').Append(Csv(r.Ssid)).Append(',')
                  .Append(r.Rssi).Append(',').Append(r.Signal).Append(',').Append(r.Channel).Append(',').Append(r.FrequencyMhz).Append(',')
                  .Append(Csv(r.Auth)).Append(',').Append(Csv(r.Encryption)).Append(',').Append(lat).Append(',').Append(lon).Append('\n');
        }
        await SaveAndShareAsync(FileNameFor(".csv"), path => File.WriteAllTextAsync(path, sb.ToString()));
    }

    private static string Csv(string s) => s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    public string FileNameFor(string extension)
    {
        var name = _survey.Name;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(' ', '_') + extension;
    }

    /// <summary>Saves a file into the export folder (Export page) and offers it to the share sheet.</summary>
    public async Task SaveAndShareAsync(string fileName, Func<string, Task> write)
    {
        try
        {
            var (folder, _) = Services.ExportLocation.Resolve();
            var saved = await Services.SaveFolder.SaveAsync(folder, fileName, write) ?? throw new IOException("Nothing was written.");
            Status = Loc.T("Common_Saved", Services.SaveFolder.Describe(saved));
            var (local, _) = await Services.SaveFolder.GetLocalFileAsync(saved);
            try { await Share.Default.RequestAsync(new ShareFileRequest { Title = _survey.Name, File = new ShareFile(local) }); }
            catch { /* saved either way */ }
        }
        catch (Exception ex)
        {
            Status = Loc.T("Common_ExportFailed", ex.Message);
        }
    }
}
