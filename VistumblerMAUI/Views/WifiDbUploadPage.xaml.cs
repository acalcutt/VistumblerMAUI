using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class WifiDbUploadPage : ContentPage
{
    public WifiDbUploadPage(WifiDbUploadViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
