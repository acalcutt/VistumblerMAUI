using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.ViewModels;

public partial class AccessPointListViewModel : ObservableObject, IQueryAttributable
{
    private readonly IDatabaseService _db;
    private List<AccessPoint> _all = new();

    [ObservableProperty] private ObservableCollection<AccessPoint> _filtered = new();
    [ObservableProperty] private AccessPoint? _selectedAp;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public AccessPointListViewModel(IDatabaseService db) => _db = db;

    /// <summary>Lets other pages (e.g. the map's "View in AP List" popup action) jump
    /// straight to a specific BSSID via `//AccessPointListPage?bssid=...`.</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("bssid", out var bssid) && bssid is string s && !string.IsNullOrWhiteSpace(s))
            SearchText = s;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _db.InitializeAsync();
        _all = await _db.GetAllAccessPointsAsync();
        ApplyFilter();
        StatusMessage = $"{_all.Count} access points";
    }

    private void ApplyFilter()
    {
        var q = SearchText?.Trim() ?? string.Empty;
        var results = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(a =>
                a.Ssid.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Bssid.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Manufacturer.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        Filtered = new ObservableCollection<AccessPoint>(results);
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        await _db.ClearAllAccessPointsAsync();
        _all.Clear();
        ApplyFilter();
        StatusMessage = "Cleared";
    }
}
