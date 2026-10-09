using VistumblerMAUI.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VistumblerMAUI.Services;
using VistumblerMAUI.Views;

namespace VistumblerMAUI.ViewModels;

/// <summary>A saved filter in the list, with whether it's the one in use.</summary>
public sealed record FilterRow(ApFilter Filter, bool IsActive)
{
    public string Name => Filter.Name;
    public string Summary => Filter.Summary;
}

/// <summary>
/// The saved filters, after the original Vistumbler's View → Filters menu: tap one to use it, or Show all APs;
/// add, edit and delete them. The filter in use narrows the AP list and is offered on the Export page.
/// </summary>
public partial class FiltersViewModel : ObservableObject
{
    public ObservableCollection<FilterRow> Filters { get; } = new();
    [ObservableProperty] private bool _noFilterActive;
    [ObservableProperty] private bool _hasFilters;

    public void Refresh()
    {
        var active = ApFilterStore.Active;
        Filters.Clear();
        foreach (var f in ApFilterStore.All) Filters.Add(new FilterRow(f, f.Id == active?.Id));
        NoFilterActive = active is null;
        HasFilters = Filters.Count > 0;
    }

    [RelayCommand]
    private async Task UseAsync(FilterRow row)
    {
        ApFilterStore.SetActive(row.Filter);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task ShowAllAsync()
    {
        ApFilterStore.SetActive(null);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private static Task AddAsync() => Shell.Current.GoToAsync($"{nameof(FilterEditPage)}?id=new");

    [RelayCommand]
    private static Task EditAsync(FilterRow row) =>
        Shell.Current.GoToAsync($"{nameof(FilterEditPage)}?id={Uri.EscapeDataString(row.Filter.Id)}");

    [RelayCommand]
    private async Task DeleteAsync(FilterRow row)
    {
        if (!await Shell.Current.DisplayAlertAsync(Loc.T("Filter_DeleteTitle"), Loc.T("Filter_DeleteQuestion", row.Name), Loc.T("Common_Delete"), Loc.T("Common_Cancel"))) return;
        ApFilterStore.Delete(row.Filter);
        Refresh();
    }
}
