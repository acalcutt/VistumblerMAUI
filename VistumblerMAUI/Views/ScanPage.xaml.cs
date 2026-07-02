using Vistumbler.Core.Models;
using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class ScanPage : ContentPage
{
    private readonly ScanViewModel _vm;

    public ScanPage(ScanViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadCommand.ExecuteAsync(null);
    }

    // Tapping a row opens the AP details page (passing the BSSID). Selection is cleared
    // so the same row can be reopened, and no row stays highlighted.
    private async void OnApSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not AccessPoint ap) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await Shell.Current.GoToAsync($"{nameof(ApDetailsPage)}?bssid={Uri.EscapeDataString(ap.Bssid)}");
    }
}
