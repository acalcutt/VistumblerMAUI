using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Models;

namespace VistumblerMAUI.ViewModels;

/// <summary>One group in the grouped AP list: its APs (none while collapsed) and a header with the count.</summary>
public sealed class ApGroup : ObservableCollection<AccessPoint>
{
    public ApGroup(string title) => Title = title;

    public string Title { get; }

    private int _total;
    private bool _expanded = true;

    /// <summary>APs in the group, including while it's collapsed.</summary>
    public int Total
    {
        get => _total;
        set { if (_total == value) return; _total = value; OnPropertyChanged(new PropertyChangedEventArgs(nameof(Header))); }
    }

    public bool IsExpanded
    {
        get => _expanded;
        set { if (_expanded == value) return; _expanded = value; OnPropertyChanged(new PropertyChangedEventArgs(nameof(Header))); }
    }

    public string Header => $"{(IsExpanded ? "▾" : "▸")}  {Title}  ({Total})";
}

/// <summary>
/// Group by on the Scan page, the phone version of the original Vistumbler's tree view: the AP list (filtered,
/// searched and sorted as usual) split into groups by channel, band, security and so on, each collapsible.
/// The groups are kept in step with the flat list in place, so the list doesn't jump about between scans.
/// </summary>
public partial class ScanViewModel
{
    private const string GroupByKey = "Scan_GroupBy";

    public IReadOnlyList<string> GroupByOptions { get; } = new[]
    {
        "No grouping", "Channel", "Band", "Security type", "Authentication", "Encryption",
        "Network type", "Manufacturer", "SSID",
    };

    [ObservableProperty] private string _selectedGroupBy = Preferences.Get(GroupByKey, "No grouping");
    [ObservableProperty] private bool _isGrouped = Preferences.Get(GroupByKey, "No grouping") != "No grouping";

    /// <summary>The grouped list, used while <see cref="IsGrouped"/>.</summary>
    public ObservableCollection<ApGroup> Groups { get; } = new();

    private readonly HashSet<string> _collapsedGroups = new();

    partial void OnSelectedGroupByChanged(string value)
    {
        Preferences.Set(GroupByKey, value);
        IsGrouped = value != "No grouping";
        _collapsedGroups.Clear();
        Groups.Clear();
        SyncGroups();
    }

    [RelayCommand]
    private void ToggleGroup(ApGroup group)
    {
        if (!_collapsedGroups.Remove(group.Title)) _collapsedGroups.Add(group.Title);
        SyncGroups();
    }

    /// <summary>The group an AP falls in, with how the groups are ordered. Channels sort by number.</summary>
    private (string Title, double Order) GroupOf(AccessPoint ap) => SelectedGroupBy switch
    {
        "Channel"       => (Localization.Loc.T("Group_Channel", ap.Channel), ap.Channel),
        "Band"          => ap.FrequencyMhz >= 5925 ? ("6 GHz", 3) : ap.FrequencyMhz >= 4900 ? ("5 GHz", 2)
                         : ap.FrequencyMhz > 0 || ap.Channel is >= 1 and <= 14 ? ("2.4 GHz", 1) : ("5 GHz", 2),
        "Security type" => Services.ApFilter.SecurityType(ap) switch { 1 => (Localization.Loc.T("Group_Open"), 1), 2 => ("WEP", 2), _ => (Localization.Loc.T("Group_Secure"), 3) },
        "Authentication" => (Named(ap.AuthText), 0),
        "Encryption"    => (Named(ap.EncryptionText), 0),
        "Network type"  => (Localization.Loc.T(ap.NetworkType == NetworkType.Adhoc ? "Group_AdHoc" : "Group_Infrastructure"), 0),
        "Manufacturer"  => (Named(ap.Manufacturer), 0),
        "SSID"          => (string.IsNullOrEmpty(ap.Ssid) ? Localization.Loc.T("Group_Hidden") : ap.Ssid, 0),
        _               => ("", 0),
    };

    private static string Named(string? text) => string.IsNullOrWhiteSpace(text) ? Localization.Loc.T("Group_Unknown") : text;

    /// <summary>
    /// Brings <see cref="Groups"/> in line with the flat list (already filtered, searched and sorted), moving
    /// rather than rebuilding where it can. Called after the flat list changes. UI thread only.
    /// </summary>
    private void SyncGroups()
    {
        if (!IsGrouped)
        {
            if (Groups.Count > 0) Groups.Clear();
            return;
        }

        var desired = AccessPoints
            .GroupBy(GroupOf)
            .OrderBy(g => g.Key.Order).ThenBy(g => g.Key.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        for (int i = 0; i < desired.Count; i++)
        {
            var title = desired[i].Key.Title;
            int at = -1;
            for (int j = i; j < Groups.Count; j++) if (Groups[j].Title == title) { at = j; break; }
            ApGroup group;
            if (at < 0) Groups.Insert(i, group = new ApGroup(title));
            else
            {
                if (at != i) Groups.Move(at, i);
                group = Groups[i];
            }

            var items = desired[i].ToList();
            group.Total = items.Count;
            group.IsExpanded = !_collapsedGroups.Contains(title);
            Reconcile(group, group.IsExpanded ? items : new List<AccessPoint>());
        }
        while (Groups.Count > desired.Count) Groups.RemoveAt(Groups.Count - 1);
    }

    // Same in-place reconcile as the flat list: remove, then move or insert into order
    private static void Reconcile(ObservableCollection<AccessPoint> target, List<AccessPoint> desired)
    {
        var wanted = new HashSet<AccessPoint>(desired);
        for (int i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i])) target.RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i])) continue;
            int existing = target.IndexOf(desired[i]);
            if (existing >= 0) target.Move(existing, i);
            else target.Insert(i, desired[i]);
        }
    }
}
