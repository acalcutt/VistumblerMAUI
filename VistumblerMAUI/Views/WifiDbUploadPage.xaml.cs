using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class WifiDbUploadPage : ContentPage
{
    private readonly WifiDbUploadViewModel _vm;

    public WifiDbUploadPage(WifiDbUploadViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    // Pick up an account set in Settings while this page was open
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Refresh();
    }
}
