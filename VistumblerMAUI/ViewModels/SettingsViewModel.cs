using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VistumblerMAUI.Views;

namespace VistumblerMAUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private int  _scanIntervalMs = 2000;
    [ObservableProperty] private bool _soundEnabled   = true;
    [ObservableProperty] private bool _gpsEnabled     = true;

    [RelayCommand]
    private async Task GoToImportAsync() => await Shell.Current.GoToAsync(nameof(ImportPage));

    [RelayCommand]
    private async Task GoToExportAsync() => await Shell.Current.GoToAsync(nameof(ExportPage));
}
