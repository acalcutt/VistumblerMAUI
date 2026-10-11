using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

/// <summary>This session's Save &amp; Clear files and the WifiDB account's totals (UploadsViewModel).</summary>
public partial class UploadsPage : ContentPage
{
    private readonly UploadsViewModel _vm;

    public UploadsPage(UploadsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}
