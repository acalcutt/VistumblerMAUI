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

/// <summary>A choice in the survey's network picker: everything, one SSID, or one AP.</summary>
public sealed record SurveyNetwork(string Label, string? Ssid, string? Bssid)
{
    public override string ToString() => Label;
}

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

    public SurveyViewModel(ScanViewModel scan) => _scan = scan;

    public void Start()
    {
        _scan.ScanCycleMerged += OnScanCycle;
        _scan.PropertyChanged += OnScanPropertyChanged;
        IsScanning = _scan.IsScanning;
        if (_survey.Marks.Count == 0 && _survey.PlanImage is null)
            Open(SurveyStore.Load(SurveyStore.LastId) ?? NewSurveyObject());
        else
            UpdateStatus();
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
        Drawable.Pending = null;
        IsWaiting = false;
        LoadPlanImage();
        RebuildNetworks();
        Refresh(refit: true);
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
            var mark = _survey.Marks[hit];
            string delete = Loc.T("Survey_DeleteSpot"), again = Loc.T("Survey_MeasureAgain");
            var choice = await Shell.Current.DisplayActionSheetAsync(Describe(hit), Loc.T("Common_Cancel"), delete, again);
            if (choice != delete && choice != again) return;
            _survey.Marks.Remove(mark);
            Save();
            RebuildNetworks();
            Refresh(refit: false);
            if (choice == delete) return;
            (x, y) = (mark.X, mark.Y);
        }
        MarkAt(x, y);
    }

    // The measured spot within a fingertip of a point, or -1
    private int SpotNear(double x, double y)
    {
        double reach = 20 / Drawable.Scale;
        int hit = -1;
        double best = reach * reach;
        for (int i = 0; i < _survey.Marks.Count; i++)
        {
            var m = _survey.Marks[i];
            double d2 = (m.X - x) * (m.X - x) + (m.Y - y) * (m.Y - y);
            if (d2 <= best) { best = d2; hit = i; }
        }
        return hit;
    }

    private string Describe(int index)
    {
        var mark = _survey.Marks[index];
        var lines = new List<string> { Loc.T("Survey_SpotInfo", index + 1, mark.Readings.Count, mark.Time.ToLocalTime().ToString("t")) };
        lines.Add(ValueAt(mark) is { } v ? $"{SelectedNetwork?.Label}: {v:0} dBm" : Loc.T("Survey_NotHeard", SelectedNetwork?.Label));
        if (mark.Readings.OrderByDescending(r => r.Rssi).FirstOrDefault() is { } top)
            lines.Add(Loc.T("Survey_StrongestAp", string.IsNullOrEmpty(top.Ssid) ? top.Bssid : top.Ssid, top.Rssi));
        return string.Join(Environment.NewLine, lines);
    }

    private void MarkAt(double x, double y)
    {
        Drawable.Pending = (x, y);
        IsWaiting = true;
        UpdateStatus();
        Redraw?.Invoke(false);
    }

    private void OnScanCycle(object? sender, IReadOnlyList<AccessPoint> heard)
    {
        if (Drawable.Pending is not { } at || heard.Count == 0) return;
        _survey.Marks.Add(new SurveyMark
        {
            X = at.X,
            Y = at.Y,
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

    partial void OnSelectedNetworkChanged(SurveyNetwork? value) => Refresh(refit: false);

    private void RebuildNetworks()
    {
        var keep = SelectedNetwork;
        var readings = _survey.Marks.SelectMany(m => m.Readings).ToList();
        var options = new List<SurveyNetwork> { Strongest };
        options.AddRange(readings.Where(r => !string.IsNullOrEmpty(r.Ssid))
            .GroupBy(r => r.Ssid)
            .Select(g => (g.Key, Aps: g.Select(r => r.Bssid).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Marks: g.Count()))
            .OrderByDescending(g => g.Marks).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new SurveyNetwork(Loc.T("Survey_SsidAps", g.Key, g.Aps), g.Key, null)));
        options.AddRange(readings
            .GroupBy(r => r.Bssid, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new SurveyNetwork($"{g.Key} {(string.IsNullOrEmpty(g.First().Ssid) ? Loc.T("Group_Hidden") : g.First().Ssid)}", null, g.Key)));

        Networks.Clear();
        foreach (var o in options) Networks.Add(o);
        SelectedNetwork = options.FirstOrDefault(o => o == keep) ?? Strongest;
    }

    /// <summary>The chosen network's RSSI at a mark: its strongest matching reading, or null if none heard it.</summary>
    private double? ValueAt(SurveyMark mark)
    {
        var n = SelectedNetwork ?? Strongest;
        var matching = mark.Readings.Where(r =>
            n.Bssid is not null ? string.Equals(r.Bssid, n.Bssid, StringComparison.OrdinalIgnoreCase)
            : n.Ssid is null || r.Ssid == n.Ssid);
        return matching.Any() ? matching.Max(r => r.Rssi) : null;
    }

    private void Refresh(bool refit)
    {
        Drawable.Marks = _survey.Marks.Select(m => (m.X, m.Y, ValueAt(m))).ToList();
        Drawable.RebuildHeatmap();
        UpdateStatus();
        Redraw?.Invoke(refit);
    }

    private void UpdateStatus()
    {
        if (IsWaiting)
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

    [RelayCommand]
    private async Task SurveyMenuAsync()
    {
        var actions = new (string Label, Func<Task> Run)[]
        {
            (Loc.T("Survey_New"),        () => { Open(NewSurveyObject()); return Task.CompletedTask; }),
            (Loc.T("Survey_Open"),       OpenSurveyAsync),
            (Loc.T("Survey_FloorPlan"),  PickPlanAsync),
            (Loc.T("Survey_UseGrid"),    () => { UseGrid(); return Task.CompletedTask; }),
            (Loc.T("Survey_Rename"),     RenameAsync),
            (Loc.T("Survey_ExportCsv"),  ExportCsvAsync),
            (Loc.T("Survey_Delete"),     DeleteAsync),
        };
        var choice = await Shell.Current.DisplayActionSheetAsync(_survey.Name, Loc.T("Common_Cancel"), null,
            actions.Select(a => a.Label).ToArray());
        if (actions.FirstOrDefault(a => a.Label == choice).Run is { } run) await run();
    }

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
            Save();
            LoadPlanImage();
            Refresh(refit: true);
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
        Save();
        LoadPlanImage();
        Refresh(refit: true);
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

    /// <summary>Every reading at every spot, one row each, with the spot's plan position.</summary>
    private async Task ExportCsvAsync()
    {
        if (_survey.Marks.Count == 0) { Status = Loc.T("Survey_NothingMeasured"); return; }
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("Spot,X,Y,Time (UTC),BSSID,SSID,RSSI,Signal,Channel,Frequency,Authentication,Encryption\n");
        for (int i = 0; i < _survey.Marks.Count; i++)
        {
            var m = _survey.Marks[i];
            foreach (var r in m.Readings)
                sb.Append(i + 1).Append(',').Append(m.X.ToString("0.#", inv)).Append(',').Append(m.Y.ToString("0.#", inv)).Append(',')
                  .Append(m.Time.ToString("yyyy-MM-dd HH:mm:ss", inv)).Append(',').Append(r.Bssid).Append(',').Append(Csv(r.Ssid)).Append(',')
                  .Append(r.Rssi).Append(',').Append(r.Signal).Append(',').Append(r.Channel).Append(',').Append(r.FrequencyMhz).Append(',')
                  .Append(Csv(r.Auth)).Append(',').Append(Csv(r.Encryption)).Append('\n');
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
