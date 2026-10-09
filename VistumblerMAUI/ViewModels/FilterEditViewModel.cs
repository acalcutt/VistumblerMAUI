using VistumblerMAUI.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VistumblerMAUI.Services;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Adds or edits one saved filter: the simple fields, and the original Vistumbler's per-field expressions under
/// Advanced rules. Edits a copy, so Cancel leaves the saved filter as it was. Opened with ?id=&lt;id&gt; or ?id=new.
/// </summary>
public partial class FilterEditViewModel : ObservableObject, IQueryAttributable
{
    [ObservableProperty] private ApFilter _filter = new();
    [ObservableProperty] private string _title = Loc.T("Filter_NewTitle");
    [ObservableProperty] private bool _showAdvanced;
    [ObservableProperty] private string _minSignalText = string.Empty;
    [ObservableProperty] private string _selectedStatus = StatusOptions[0];

    public static IReadOnlyList<string> StatusOptions { get; } = new[] { "Active and dead APs", "Active APs only", "Dead APs only" };
    public IReadOnlyList<string> StatusChoices => StatusOptions;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var id = query.TryGetValue("id", out var raw) ? Uri.UnescapeDataString(raw as string ?? "") : "new";
        var saved = ApFilterStore.All.FirstOrDefault(f => f.Id == id);
        Filter = saved?.Clone() ?? new ApFilter { Name = Loc.T("Filter_DefaultName", ApFilterStore.All.Count + 1) };
        Title = saved is null ? Loc.T("Filter_NewTitle") : Loc.T("Filter_EditTitle", saved.Name);
        MinSignalText = Filter.MinSignal > 0 ? Filter.MinSignal.ToString() : string.Empty;
        SelectedStatus = StatusOptions[(int)Filter.Status];
        ShowAdvanced = Filter.HasAdvanced;
    }

    partial void OnMinSignalTextChanged(string value) =>
        Filter.MinSignal = int.TryParse(value, out var n) ? Math.Clamp(n, 0, 100) : 0;

    partial void OnSelectedStatusChanged(string value)
    {
        int i = StatusOptions.ToList().IndexOf(value);
        if (i >= 0) Filter.Status = (FilterStatus)i;
    }

    [RelayCommand]
    private async Task SaveAndUseAsync()
    {
        if (!Validate()) return;
        ApFilterStore.Save(Filter);
        ApFilterStore.SetActive(Filter);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Validate()) return;
        ApFilterStore.Save(Filter);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private static Task CancelAsync() => Shell.Current.GoToAsync("..");

    private bool Validate()
    {
        Filter.Name = string.IsNullOrWhiteSpace(Filter.Name) ? "Filter" : Filter.Name.Trim();
        return true;
    }
}
