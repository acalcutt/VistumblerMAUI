using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    public SettingsPage(SettingsViewModel vm, ScanViewModel scan)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        ScanBar.BindingContext = scan;   // shared control bar reflects the live scan state
    }

    // Refresh the WifiDB fields when returning to this page (e.g. after the QR scanner
    // persisted new credentials via WifiDbSettings).
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Reload();
        _vm.WatchUploadQueue(true);
        _vm.WatchWigleQueue(true);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.WatchUploadQueue(false);
        _vm.WatchWigleQueue(false);
    }
}

