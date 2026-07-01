using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    // Refresh the WifiDB fields when returning to this page (e.g. after the QR scanner
    // persisted new credentials via WifiDbSettings).
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Reload();
    }
}

